# tags_and_events.md (DCB + 型で状態を表す版)

このドキュメントは「会議室予約＋設備＋承認（ロール承認）」サンプルを **Dynamic Consistency Boundary (DCB)** 前提で再設計したものです。

ポイントは 3 つです。

1. **1イベント = 1つの事実**  
   その事実を必要な **複数集約へタグ付け（tags）** することで、フレームワークが一貫性境界を “動的に” 構成する。

2. **ポリシー/サガは “進行管理” に寄せる**  
   重要な整合性（Hold/Approval/Confirm/Cancel/Expire）は **Committed 系の単一イベント**で保証する。

3. **状態を型で表す（Null を排除）**  
   TagState / AggregateState を **Discriminated Union（C# の record + 継承）** で表現し、  
   「null かどうか」ではなく **状態型そのもの**で許可/不許可を決める。

---

## 1. DCB の中心概念: Tag（タグ）

### 1.1 AggregateTag
イベントは `tags` を持ち、これが「この事実がどの集約に関係するか」を定義します。

- `aggregateType: string` 例: `"Reservation" | "EquipmentPool" | "ApprovalRequest" | "Room" | "UserDirectory" | "UserAccess"`
- `aggregateId: string`
- `role?: string`（任意：`"primary" | "secondary"` など。説明用）

### 1.2 DCB の意味（このサンプル）
- 例: 予約確定という “単一の事実” を **1イベント**で記録し、
  - Reservation(予約) と EquipmentPool(設備) の両方にタグ付けする。
- フレームワークはそのイベントを「同一の一貫性境界」の中で適用できるため、
  - “予約は確定したが設備は未確保” のような部分成功を原理的に避ける（または回復容易にする）。

---

## 2. 集約（Aggregates）一覧（DCB 前提）

> DCB は「集約を消す」のではなく、**不可分な事実をイベントとして統合**し、
> そのイベントを複数集約に “同時に関係するもの” としてタグ付けする設計です。

### 2.1 Room（会議室）
- マスタと利用ルール（営業時間、最大時間、承認必須フラグ等）
- DCB 対象ではない（単一集約で十分）

### 2.2 EquipmentPool（設備プール）
- 拠点単位の設備在庫と、時間帯ごとの hold/allocation を管理
- DCB 対象：Reservation の Hold / Confirm / Cancel / Expire と不可分に動く

### 2.3 Reservation（予約）
- 予約ライフサイクルの主役
- DCB 対象：Hold / Approval / Confirm / Cancel / Expire が不可分

### 2.4 ApprovalRequest（承認依頼）
- 承認フローの状態（Pending/Approved/Rejected/Expired/Cancelled）
- DCB 対象：Approval の開始・決定・終了が Reservation と不可分

### 2.5 UserDirectory（ユーザー名簿）
- 認証未連携でも動くユーザー台帳
- DCB 対象外（単一集約で十分）

### 2.6 UserAccess（権限）
- ロール付与（例: `Employee`, `FacilitiesApprover`）
- DCB 対象外（単一集約で十分）

---

## 3. イベントモデル（DCB 前提: 1イベント=1事実）

### 3.1 Event Envelope（共通）
- `eventId: string`
- `eventType: string`
- `occurredAtUtc: string`
- `actorUserId: string`（system なら `"system"`）
- `correlationId?: string`
- `causationId?: string`
- `tags: AggregateTag[]`
- `payload: object`

---

## 4. Shared Payload Types

### 4.1 TimeRange
- `startUtc: string`
- `endUtc: string`

### 4.2 EquipmentQuantities
- `items?: { [equipmentType: string]: number }`  
  例: `{ "Projector": 1, "Mic": 2 }`

### 4.3 HoldKind
- `"Normal"`（短TTL）
- `"Approval"`（長TTL）

---

## 5. Event Catalog（DCB 版）

### 5.1 Master / Admin（単一集約）

#### RoomRegistered
**tags**: `Room(roomId)`
**payload**:
- `roomId, name, location, capacity, isActive`
- `openHours?: { startLocal, endLocal, timeZone }`
- `maxDurationMinutes?: number`
- `alwaysRequiresApproval?: boolean`

#### RoomUpdated
**tags**: `Room(roomId)`
**payload**:
- `roomId`
- `patch: { name?, location?, capacity?, isActive?, openHours?, maxDurationMinutes?, alwaysRequiresApproval? }`

#### RoomDeactivated / RoomReactivated
**tags**: `Room(roomId)`
**payload**: `roomId, reason?`

---

#### EquipmentPoolCreated
**tags**: `EquipmentPool(poolId)`
**payload**:
- `poolId, location`
- `initialStock: { [type]: number }`

#### EquipmentStockAdjusted
**tags**: `EquipmentPool(poolId)`
**payload**:
- `poolId`
- `adjustments: { [type]: number }`
- `reason`

---

#### UserRegistered / UserProfileUpdated / UserDeactivated / UserReactivated
**tags**: `UserDirectory(userId)`
**payload**: 名簿情報（displayName/email/department/isActive 等）

#### UserAccessGranted / UserRoleGranted / UserRoleRevoked / UserAccessDeactivated / UserAccessReactivated
**tags**: `UserAccess(userId)`
**payload**: roles / isActive

---

### 5.2 Domain Committed Events（複数集約にタグ付けされる “事実イベント”）

#### ReservationDraftCreated
**事実**: 「予約の下書きが作成された」  
**tags**: `Reservation(reservationId)`  
**payload**:
- `reservationId, roomId, timeRange, organizerUserId`
- `title?`

#### ReservationDetailsUpdated
**事実**: 「予約の詳細が更新された」  
**tags**: `Reservation(reservationId)`  
**payload**:
- `reservationId`
- `patch: { title?, description?, attendeeCount?, hasExternalGuests?, requiredEquipment? }`

---

#### ReservationHoldCommitted
**事実**: 「予約に必要な設備が（必要なら）仮確保され、Held になった（または失敗した）」  
**tags**:
- 成功: `Reservation(reservationId)`, `EquipmentPool(poolId)`
- 失敗: 最小は `Reservation(reservationId)` のみでも可（設備側が変化しないなら）
**payload**:
- `reservationId, roomId, poolId, timeRange`
- `requestedEquipment?: { [type]: number }`
- `result: "Succeeded" | "Failed"`
- `failureReason?: string`
- 成功時のみ:
  - `holdId: string`
  - `holdKind: HoldKind`
  - `expiresAtUtc: string`

---

#### ApprovalFlowStarted
**事実**: 「承認が必要と判定され、承認依頼が開始された（Reservation は PendingApproval になった）」  
**tags**: `Reservation(reservationId)`, `ApprovalRequest(approvalRequestId)`  
**payload**:
- `reservationId, approvalRequestId`
- `requiredRole: string`（例: `"FacilitiesApprover"`）
- `deadlineUtc: string`
- `reasons: string[]`
- `note?`

---

#### ApprovalDecisionRecorded
**事実**: 「承認依頼に対して承認/却下が確定した」  
**tags**: `ApprovalRequest(approvalRequestId)`, `Reservation(reservationId)`  
**payload**:
- `approvalRequestId, reservationId`
- `decision: "Approved" | "Rejected"`
- `decidedByUserId: string`
- `decidedAtUtc: string`
- `reason?: string`（Rejected の場合は必須推奨）
- `note?`

> 注: “承認されたがまだ確定していない” 状態を、Reservation の状態型で表せるようにする（後述）。

---

#### ReservationConfirmedCommitted
**事実**: 「予約が確定し、必要な設備割当も確定した」  
**tags**: `Reservation(reservationId)`, `EquipmentPool(poolId)`  
**payload**:
- `reservationId, roomId, poolId, timeRange`
- `confirmedAtUtc: string`
- `allocatedEquipment?: { [type]: number }`
- `sourceHoldId?: string`
- `releaseHold?: boolean`（通常 true）

---

#### ReservationCancelledCommitted
**事実**: 「予約がキャンセルされ、必要な解放（hold/allocation/approval cancel）が同時に成立した」  
**tags（状況に応じて）**:
- 常に `Reservation(reservationId)`
- 設備を持つなら `EquipmentPool(poolId)`
- 承認フロー中なら `ApprovalRequest(approvalRequestId)`
**payload**:
- `reservationId`
- `cancelledByUserId: string`
- `cancelledAtUtc: string`
- `reason?: string`
- 設備解放情報（必要なものだけ）
  - `poolId?: string`
  - `holdId?: string`
  - `allocationReleased?: boolean`
- 承認終了情報
  - `approvalRequestId?: string`
  - `approvalCancelled?: boolean`

---

#### ReservationExpiredCommitted
**事実**: 「予約が期限切れで失効し、必要な解放（hold / approval expiry）が同時に成立した」  
**tags（状況に応じて）**:
- 常に `Reservation(reservationId)`
- `EquipmentPool(poolId)`（hold 解放が必要なら）
- `ApprovalRequest(approvalRequestId)`（承認期限切れなら）
**payload**:
- `reservationId`
- `expiredAtUtc: string`
- `reason: "HoldExpired" | "ApprovalExpired"`
- `poolId?: string`
- `holdId?: string`
- `approvalRequestId?: string`

---

## 6. 状態を型で表す（TagState / AggregateState の Discriminated Union）

ここからが “昇華” の本体です。  
**null で状態を表現しない**ために、状態を `abstract record` + 派生 record で表現します。

> C# の DU は完全ではありませんが、  
> `abstract record` + 派生 record + `switch` 式で運用すると “null のない状態” を作れます。

### 6.1 例: Reservation の状態型（Null を消す）
Reservation は “状態遷移が重要” なので DU の効果が最大です。

```csharp
public abstract record ReservationState;

public sealed record Draft(
    string ReservationId,
    string RoomId,
    TimeRange TimeRange,
    string OrganizerUserId,
    ReservationDetails Details
) : ReservationState;

public sealed record Held(
    string ReservationId,
    string RoomId,
    TimeRange TimeRange,
    string OrganizerUserId,
    ReservationDetails Details,
    EquipmentHold Hold
) : ReservationState;

public sealed record PendingApproval(
    string ReservationId,
    string RoomId,
    TimeRange TimeRange,
    string OrganizerUserId,
    ReservationDetails Details,
    EquipmentHold Hold,
    ApprovalRef Approval
) : ReservationState;

/// <summary>
/// 承認済みだが、まだ確定(Confirmed)していない状態を型で表す。
/// </summary>
public sealed record ApprovedAwaitingConfirm(
    string ReservationId,
    string RoomId,
    TimeRange TimeRange,
    string OrganizerUserId,
    ReservationDetails Details,
    EquipmentHold Hold,
    ApprovalRef Approval,
    ApprovalDecisionInfo Decision
) : ReservationState;

public sealed record Confirmed(
    string ReservationId,
    string RoomId,
    TimeRange TimeRange,
    string OrganizerUserId,
    ReservationDetails Details,
    EquipmentAllocation Allocation,
    ConfirmInfo Confirm
) : ReservationState;

public sealed record Rejected(
    string ReservationId,
    string RoomId,
    TimeRange TimeRange,
    string OrganizerUserId,
    ReservationDetails Details,
    EquipmentHold Hold,
    ApprovalRef Approval,
    RejectInfo Reject
) : ReservationState;

public sealed record Cancelled(
    string ReservationId,
    string RoomId,
    TimeRange TimeRange,
    string OrganizerUserId,
    ReservationDetails Details,
    CancelInfo Cancel
) : ReservationState;

public sealed record Expired(
    string ReservationId,
    string RoomId,
    TimeRange TimeRange,
    string OrganizerUserId,
    ReservationDetails Details,
    ExpireInfo Expire
) : ReservationState;

public record ReservationDetails(
    string? Title,
    string? Description,
    int? AttendeeCount,
    bool HasExternalGuests,
    IReadOnlyDictionary<string,int> RequiredEquipment
);

public record EquipmentHold(string PoolId, string HoldId, HoldKind HoldKind, DateTime ExpiresAtUtc);
public record EquipmentAllocation(string PoolId, IReadOnlyDictionary<string,int> AllocatedEquipment, string? SourceHoldId);
public record ApprovalRef(string ApprovalRequestId, string RequiredRole, DateTime DeadlineUtc, IReadOnlyList<string> Reasons);
public record ApprovalDecisionInfo(string DecidedByUserId, DateTime DecidedAtUtc);
public record ConfirmInfo(DateTime ConfirmedAtUtc, string ActorUserId);
public record RejectInfo(string RejectedByUserId, DateTime RejectedAtUtc, string Reason);
public record CancelInfo(string CancelledByUserId, DateTime CancelledAtUtc, string? Reason);
public record ExpireInfo(DateTime ExpiredAtUtc, string Reason);

狙い
	•	PendingApproval のとき、ApprovalRef は必ず存在（null 不要）
	•	Held のとき、EquipmentHold は必ず存在（null 不要）
	•	Confirmed のとき、EquipmentAllocation は必ず存在（null 不要）
	•	「承認済みだが確定前」を ApprovedAwaitingConfirm で明示できる

⸻

6.2 例: ApprovalRequest の状態型

public abstract record ApprovalRequestState;

public sealed record Pending(
    string ApprovalRequestId,
    string ReservationId,
    string RequiredRole,
    DateTime DeadlineUtc,
    IReadOnlyList<string> Reasons
) : ApprovalRequestState;

public sealed record Approved(
    string ApprovalRequestId,
    string ReservationId,
    string RequiredRole,
    DateTime DeadlineUtc,
    IReadOnlyList<string> Reasons,
    string DecidedByUserId,
    DateTime DecidedAtUtc
) : ApprovalRequestState;

public sealed record Rejected(
    string ApprovalRequestId,
    string ReservationId,
    string RequiredRole,
    DateTime DeadlineUtc,
    IReadOnlyList<string> Reasons,
    string DecidedByUserId,
    DateTime DecidedAtUtc,
    string RejectReason
) : ApprovalRequestState;

public sealed record Expired(/* ... */) : ApprovalRequestState;
public sealed record Cancelled(/* ... */) : ApprovalRequestState;


⸻

6.3 例: EquipmentPool の状態型（最小）

EquipmentPool は “在庫 + 時間帯の確保/割当” のため、完全な DU より
「辞書で管理 + 重要な参照を型にする」方が実装が簡単です。
ただし null 排除は可能です。

public record EquipmentPoolState(
    string PoolId,
    string Location,
    IReadOnlyDictionary<string,int> Stock,
    IReadOnlyDictionary<string, HoldRecord> HoldsByHoldId,
    IReadOnlyDictionary<string, AllocationRecord> AllocationsByReservationId
);

public record HoldRecord(string ReservationId, TimeRange TimeRange, IReadOnlyDictionary<string,int> Items, HoldKind HoldKind, DateTime ExpiresAtUtc);
public record AllocationRecord(TimeRange TimeRange, IReadOnlyDictionary<string,int> Items, string? SourceHoldId);


⸻

7. 型で “許可/不許可” を決める（null を見ない）

7.1 例: Confirm が可能か？

Reservation の状態型で分岐します。

bool CanConfirm(ReservationState state) => state switch
{
    Held => true,
    ApprovedAwaitingConfirm => true,
    _ => false
};

7.2 例: Approve が可能か？

ApprovalRequest は Pending のときのみ。

bool CanApprove(ApprovalRequestState state) => state is Pending;


⸻

8. イベント適用（Apply）の考え方（DCB + DU）

8.1 “同じイベント” を複数集約が Apply する
	•	DCB イベントは tags に複数集約が入る
	•	フレームワークはそれぞれの集約（TagState）に同じ event を適用する

8.2 Apply は “状態型遷移” を表現する

例: ReservationHoldCommitted を ReservationState に適用
	•	Draft → Held（Succeeded）
	•	Draft → Draft（Failed の場合、状態は維持しつつエラーは ReadModel に反映でもよい）
	•	それ以外 → 例外（不正遷移）として弾く

⸻

9. このサンプルで DCB に統合した “事実イベント” の一覧
	•	ReservationHoldCommitted（Reservation + EquipmentPool）
	•	ApprovalFlowStarted（Reservation + ApprovalRequest）
	•	ApprovalDecisionRecorded（ApprovalRequest + Reservation）
	•	ReservationConfirmedCommitted（Reservation + EquipmentPool）
	•	ReservationCancelledCommitted（Reservation + EquipmentPool (+ApprovalRequest)）
	•	ReservationExpiredCommitted（Reservation + EquipmentPool (+ApprovalRequest)）

この設計により、整合性が必要な局面は 「1イベント=1事実」 として表現され、
フレームワークが タグ集合=DCB を使って多集約の一貫性を確保できます。

次のステップとして、同じ思想（DCB + DU）で `command.md` を書き直します。  
ポイントは「コマンドは *今の状態型* を要求する（例: `ConfirmReservation(Held or ApprovedAwaitingConfirm)` のみ受理）」のように、**許可条件を型で表現**する形にすることです。
