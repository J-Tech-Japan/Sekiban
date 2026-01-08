# command.md (DCB + 型で状態を表す版)

このドキュメントは「会議室予約＋設備＋承認（ロール承認）」サンプルの **DCB（Dynamic Consistency Boundary）** 前提のコマンド設計です。

DCB 前提では、重要な整合性（Hold / Approval / Confirm / Cancel / Expire）は
**“1イベント=1事実” の Committed イベント**で表現され、1つのイベントが複数集約にタグ付けされます。

さらに本設計では、**状態を型（Discriminated Union）で表す**ことにより、
- null の有無で分岐しない
- “状態型” を見て許可/不許可を決定できる
ことを狙います。

---

## 0. 前提: コマンドの入出力モデル

### 0.1 共通メタデータ（全コマンド）
- `actorUserId: string`（操作主体）
- `correlationId?: string`
- `commandId?: string`（冪等性・重複排除に使うなら）

### 0.2 “DCB コマンド” の形
DCB の中核となるコマンド（Hold/Approval/Confirm/Cancel/Expire）は、
**単一コマンドが単一イベントを生成し、そのイベントが複数集約へタグ付けされる**設計にする。

- 入力: 必要な事実（reservationId/poolId/approvalRequestId 等）
- 出力: 1つの Committed Event（tags を含む）

---

## 1. 状態型（DU）とコマンドの関係

### 1.1 “状態型”でガードする（null を見ない）
実装上、コマンドハンドラは `TagState<TState>`（または AggregateState）を受け取り、
`switch` / `is` で許可する状態を限定する。

例（概念）:
- `ConfirmReservation` は `Held` または `ApprovedAwaitingConfirm` のときのみ許可
- `Approve` は `ApprovalRequestState.Pending` のときのみ許可
- `StartApprovalFlow` は `Held` のときのみ許可

> これにより、「holdId が null だから未hold」などの判定を排除できる。

---

## 2. Commands（分類）

### 2.1 Admin / Master Commands（単一集約）
- Room / EquipmentPool / UserDirectory / UserAccess を管理するためのコマンド  
（DCB の対象外。通常の集約コマンド）

### 2.2 DCB Domain Commands（複数集約に跨る事実を作る）
- Reservation と EquipmentPool / ApprovalRequest の整合性を要求するコマンド  
（Hold / StartApproval / DecideApproval / Confirm / Cancel / Expire）

---

# 3. Admin / Master Commands（単一集約）

## 3.1 Room（Aggregate: Room）

### RegisterRoom
**Allowed when**: まだ存在しない  
**Emits**: `RoomRegistered`（tags: Room）
- `roomId: string`
- `name: string`
- `location: string`
- `capacity: number`
- `openHours?: { startLocal: string, endLocal: string, timeZone: string }`
- `maxDurationMinutes?: number`
- `alwaysRequiresApproval?: boolean`
- `actorUserId: string`

### UpdateRoom
**Emits**: `RoomUpdated`
- `roomId: string`
- `patch: { ... }`
- `actorUserId: string`

### DeactivateRoom / ReactivateRoom
**Emits**: `RoomDeactivated` / `RoomReactivated`
- `roomId: string`
- `reason?: string`
- `actorUserId: string`

---

## 3.2 EquipmentPool（Aggregate: EquipmentPool）

### CreateEquipmentPool
**Emits**: `EquipmentPoolCreated`
- `poolId: string`
- `location: string`
- `initialStock: { [type: string]: number }`
- `actorUserId: string`

### AdjustEquipmentStock
**Emits**: `EquipmentStockAdjusted`
- `poolId: string`
- `adjustments: { [type: string]: number }`
- `reason: string`
- `actorUserId: string`

---

## 3.3 UserDirectory（Aggregate: UserDirectory）

### RegisterUser
**Emits**: `UserRegistered`
- `userId: string`
- `displayName: string`
- `email?: string`
- `department?: string`
- `actorUserId: string`

### UpdateUserProfile
**Emits**: `UserProfileUpdated`
- `userId: string`
- `patch: { displayName?, email?, department? }`
- `actorUserId: string`

### DeactivateUser / ReactivateUser
**Emits**: `UserDeactivated` / `UserReactivated`
- `userId: string`
- `reason?: string`
- `actorUserId: string`

---

## 3.4 UserAccess（Aggregate: UserAccess）

### GrantUserAccess
**Emits**: `UserAccessGranted`
- `userId: string`
- `initialRoles: string[]`
- `actorUserId: string`

### GrantRole / RevokeRole
**Emits**: `UserRoleGranted` / `UserRoleRevoked`
- `userId: string`
- `role: string`
- `reason?: string`
- `actorUserId: string`

### DeactivateUserAccess / ReactivateUserAccess
**Emits**: `UserAccessDeactivated` / `UserAccessReactivated`
- `userId: string`
- `reason?: string`
- `actorUserId: string`

---

# 4. Reservation Commands（単一集約: 下書き・編集）

DCB 前提でも、下書き作成・編集は Reservation 単独で行うのが自然です。

## CreateReservationDraft
**Allowed state**: まだ存在しない  
**Emits**: `ReservationDraftCreated`（tags: Reservation）
- `reservationId: string`
- `roomId: string`
- `timeRange: { startUtc: string, endUtc: string }`
- `organizerUserId: string`
- `title?: string`
- `actorUserId: string`

## UpdateReservationDetails
**Allowed state**: `Draft | Held | PendingApproval | ApprovedAwaitingConfirm`  
（Confirmed は不可、Cancelled/Expired/Rejected は不可）  
**Emits**: `ReservationDetailsUpdated`（tags: Reservation）
- `reservationId: string`
- `patch: {
    title?: string,
    description?: string,
    attendeeCount?: number,
    hasExternalGuests?: boolean,
    requiredEquipment?: { [type: string]: number }
  }`
- `actorUserId: string`

---

# 5. DCB Domain Commands（複数集約に跨る “Committed イベント” を発行）

ここがサンプルのメインです。
各コマンドは **1つの事実イベント**を生成し、
そのイベントに **複数の tags** を含めます。

---

## 5.1 CommitReservationHold（仮確保コミット）

### CommitReservationHold
**Purpose**
- Reservation を Held に進めるために、必要なら設備を仮確保する
- 結果（成功/失敗）を **1つの事実イベント**として記録する

**Allowed state（型でガード）**
- `ReservationState.Draft` のみ

**Reads**
- Reservation（Draft）
- Room（営業時間/最大時間/承認必須などのルール確認に使うなら）
- EquipmentPool（在庫/時間帯重複を評価）

**Emits (single event)**
- `ReservationHoldCommitted`
  - tags: `Reservation(reservationId)` + 成功時は `EquipmentPool(poolId)`

**Command payload**
- `reservationId: string`
- `poolId: string`（拠点に1つなら固定でも可）
- `actorUserId: string`

> requestedEquipment/timeRange 等は Reservation(Draft) の状態から取得する想定
> （コマンドに重複入力させない）

---

## 5.2 StartApprovalFlow（承認フロー開始コミット）

### StartApprovalFlow
**Purpose**
- 承認が必要な Reservation を PendingApproval にし、ApprovalRequest を生成する
- “承認フロー開始” を **1つの事実イベント**として記録する

**Allowed state**
- `ReservationState.Held` のみ

**Reads**
- Reservation（Held）
- Room（承認条件の一部にするなら）
- （任意）ポリシー: 承認要否・理由・期限・requiredRole を計算

**Emits (single event)**
- `ApprovalFlowStarted`
  - tags: `Reservation(reservationId)` + `ApprovalRequest(approvalRequestId)`

**Command payload**
- `reservationId: string`
- `approvalRequestId: string`（ULIDを呼び出し側生成でもフレームワーク生成でもOK）
- `requiredRole?: string`（省略時はポリシー既定: "FacilitiesApprover"）
- `deadlineUtc?: string`（省略時はポリシーで決定）
- `note?: string`
- `actorUserId: string`

> “承認が不要” の場合はこのコマンド自体を UI が出さない/呼ばない。
> もしくはハンドラが state とポリシーで弾く。

---

## 5.3 RecordApprovalDecision（承認/却下コミット）

### RecordApprovalDecision
**Purpose**
- ApprovalRequest に対する決定（Approved/Rejected）を確定し、
  Reservation 側にも同じ決定を反映する
- “決定” を **1つの事実イベント**として記録する

**Allowed state（型でガード）**
- ApprovalRequest: `Pending` のみ
- Reservation: `PendingApproval` のみ（ApprovalRequest と整合すること）

**Authorization**
- `actorUserId` が `requiredRole`（例: FacilitiesApprover）を持つこと
  - UserAccess を参照して判定（または API 層で先に弾く）

**Emits (single event)**
- `ApprovalDecisionRecorded`
  - tags: `ApprovalRequest(approvalRequestId)` + `Reservation(reservationId)`

**Command payload**
- `approvalRequestId: string`
- `reservationId: string`
- `decision: "Approved" | "Rejected"`
- `reason?: string`（Rejected の場合は必須推奨）
- `note?: string`
- `decidedAtUtc?: string`（省略時は現在時刻）
- `actorUserId: string`

> このイベント適用により Reservation は
> - Approved → `ApprovedAwaitingConfirm` に遷移
> - Rejected → `Rejected` に遷移
> のように “状態型” が変わる。

---

## 5.4 CommitReservationConfirmation（確定コミット）

### CommitReservationConfirmation
**Purpose**
- 予約を確定し、設備割当も確定する
- “確定” を **1つの事実イベント**として記録する

**Allowed state（型でガード）**
- `ReservationState.Held`（承認不要のケース）
- `ReservationState.ApprovedAwaitingConfirm`（承認済みのケース）

**Reads**
- Reservation（Held or ApprovedAwaitingConfirm）
- EquipmentPool（在庫/時間帯の衝突を評価し Allocation を確定）
  - 通常は hold を元に allocate する（releaseHold=true）

**Emits (single event)**
- `ReservationConfirmedCommitted`
  - tags: `Reservation(reservationId)` + `EquipmentPool(poolId)`

**Command payload**
- `reservationId: string`
- `actorUserId: string`

> poolId / timeRange / requiredEquipment / sourceHoldId 等は Reservation の状態から取得。

---

## 5.5 CommitReservationCancellation（キャンセルコミット）

### CommitReservationCancellation
**Purpose**
- 予約キャンセルに伴う解放を “同一事実” としてコミットする
  - Hold 解放 / Allocation 解放 / 承認依頼キャンセル（該当するものだけ）
- “キャンセル” を **1つの事実イベント**として記録する

**Allowed state（型でガード）**
- `ReservationState.Draft`
- `ReservationState.Held`
- `ReservationState.PendingApproval`
- `ReservationState.ApprovedAwaitingConfirm`
- `ReservationState.Confirmed`
（既に Cancelled/Expired/Rejected は不可）

**Reads**
- Reservation（状態型から、解放すべき対象が型として取れる）
- EquipmentPool（Hold/Allocation を持つ場合）
- ApprovalRequest（Pending の場合のみ Cancel する等）

**Emits (single event)**
- `ReservationCancelledCommitted`
  - tags: 常に Reservation
  - 状態に応じて EquipmentPool / ApprovalRequest も含む

**Command payload**
- `reservationId: string`
- `reason?: string`
- `actorUserId: string`

---

## 5.6 CommitReservationExpiration（期限切れコミット / system）

### CommitReservationExpiration
**Purpose**
- hold 期限切れ / 承認期限切れ を検知したときに失効と解放を同時に記録
- “期限切れ失効” を **1つの事実イベント**として記録する

**Allowed state**
- `ReservationState.Held`（hold expired）
- `ReservationState.PendingApproval`（approval expired）
- `ReservationState.ApprovedAwaitingConfirm`（approval deadline 超過などポリシー次第）

**Emits (single event)**
- `ReservationExpiredCommitted`
  - tags: Reservation + （必要に応じて）EquipmentPool / ApprovalRequest

**Command payload**
- `reservationId: string`
- `reason: "HoldExpired" | "ApprovalExpired"`
- `expiredAtUtc?: string`
- `actorUserId: string`（通常 "system"）

---

# 6. 型で “許可/不許可” を定義する（ガードの例）

コマンドが要求する状態を「型」で表すと、実装は次のような形になります。

例: Confirm

```csharp
public static bool CanConfirm(ReservationState state) => state switch
{
    Held => true,
    ApprovedAwaitingConfirm => true,
    _ => false
};

例: StartApprovalFlow

public static bool CanStartApproval(ReservationState state) => state is Held;

例: RecordApprovalDecision

public static bool CanDecide(ApprovalRequestState state) => state is Pending;


⸻

7. どのコマンドが “どの Committed Event” を出すか（対応表）
	•	CommitReservationHold
	•	→ ReservationHoldCommitted（tags: Reservation + EquipmentPool(成功時)）
	•	StartApprovalFlow
	•	→ ApprovalFlowStarted（tags: Reservation + ApprovalRequest）
	•	RecordApprovalDecision
	•	→ ApprovalDecisionRecorded（tags: ApprovalRequest + Reservation）
	•	CommitReservationConfirmation
	•	→ ReservationConfirmedCommitted（tags: Reservation + EquipmentPool）
	•	CommitReservationCancellation
	•	→ ReservationCancelledCommitted（tags: Reservation + EquipmentPool? + ApprovalRequest?）
	•	CommitReservationExpiration
	•	→ ReservationExpiredCommitted（tags: Reservation + EquipmentPool? + ApprovalRequest?）

⸻

8. 実装メモ（サンプルとしての簡潔さ）
	•	下書き（Draft）周辺は単一集約で済ませる
→ DCB は “整合性が必要な局面” に集中させると理解しやすい。
	•	重要な局面は “Committed イベント” に統合する
→ ReadModel が単純になり、DCB の価値が伝わる。
	•	状態は DU で表し、null で状態を表現しない
→ Guard が読みやすく、誤実装が減る。

必要なら次に、`tags_and_events.md` の状態型（ReservationState / ApprovalRequestState）に合わせて  
**各 Committed Event の Apply（状態遷移表）**を 1ページで追加できます。  
「どのイベントで、どの状態型からどの状態型へ遷移するか」が揃うと、実装がさらに一直線になります。