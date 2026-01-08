# readmodels.md (DCB version)

このドキュメントは「会議室予約＋設備＋承認（ロール承認）」サンプルの Read Model（投影/クエリモデル）定義です。
本サンプルは **DCB（Dynamic Consistency Boundary）** を採用しており、1つのイベントが複数集約にタグ付けされます。

Read Model は「集約の内部状態」ではなく、UI/検索/一覧表示に最適化した形で構築します。

---

## 0. DCB と Read Model の関係

DCB では、重要な事実が「1イベント」として記録され、そのイベントが `tags` により複数集約へ紐付くため、
Read Model 側では次のメリットが得られます。

- **矛盾しにくい**  
  例: `ReservationConfirmedCommitted` は Reservation と EquipmentPool に同時適用されるため、
  「予約は確定したが設備が確定していない」状態を Read Model が観測しづらい。
- **投影ロジックが単純**  
  「どちらの集約イベントを先に受けるか」ではなく、「単一の事実イベントをどう反映するか」になる。
- **トレースが容易**  
  `eventId / correlationId` を軸に「この確定は何が原因か」を追える。

---

## 1. Query Use Cases（画面/機能の最小セット）

社内アプリとして、認証だけ付ければ使えるレベルを想定すると、最低限この3つが揃うと実用感が出ます。

1) **会議室カレンダー**（部屋別の予約一覧・状態）
2) **承認待ち一覧**（FacilitiesApprover ロール向け）
3) **設備の利用状況**（時間帯ごとの確保/割当、残数）

加えて、運用のために最低限のマスタ/ユーザー系もあると良いです。

4) 部屋一覧（Room Directory）
5) 設備一覧（Equipment Directory / 在庫）
6) ユーザー一覧（User Directory）
7) ユーザー権限一覧（User Access）

---

## 2. Read Model 1: RoomCalendarView（会議室カレンダー）

### 2.1 目的
- 部屋ごとの予約をカレンダー表示
- 状態（Draft/Held/PendingApproval/Confirmed/Cancelled/Expired/Rejected）を表示
- 予約詳細（タイトル、主催者、人数、設備オプション）を表示

### 2.2 形（推奨スキーマ）
**RoomCalendarEntry**
- `roomId: string`
- `reservationId: string`
- `startUtc: string`
- `endUtc: string`
- `status: string`  
  `Draft | Held | PendingApproval | Confirmed | Cancelled | Expired | Rejected`
- `title?: string`
- `organizerUserId: string`
- `organizerDisplayName?: string`（UserDirectory投影でJOIN/デノーマライズ）
- `attendeeCount?: number`
- `hasExternalGuests?: boolean`
- `requiredEquipment?: { [equipmentType: string]: number }`
- `approvalRequestId?: string`
- `approvalRequiredRole?: string`
- `approvalDeadlineUtc?: string`
- `lastEventId: string`
- `lastUpdatedAtUtc: string`

### 2.3 主な入力イベント（DCB）
- `ReservationDraftCreated`
- `ReservationDetailsUpdated`
- `ReservationHoldCommitted`
- `ApprovalFlowStarted`
- `ApprovalDecisionRecorded`
- `ReservationConfirmedCommitted`
- `ReservationCancelledCommitted`
- `ReservationExpiredCommitted`
-（optional）`ReservationRescheduledCommitted`

### 2.4 投影ルール（要点）
- `ReservationDraftCreated`:
  - Entry を作成（status=Draft）
- `ReservationDetailsUpdated`:
  - 該当 Entry を patch
- `ReservationHoldCommitted`:
  - `result == "Succeeded"` → status=Held, hold 情報を Entry に反映（必要なら）
  - `result == "Failed"` → status は Draft のままでもよい（UIはエラーで表現）
- `ApprovalFlowStarted`:
  - status=PendingApproval、`approvalRequestId/requiredRole/deadline` をセット
- `ApprovalDecisionRecorded`:
  - decision=Approved → status は PendingApproval のまま（確定前）
  - decision=Rejected → status=Rejected（理由も必要なら別カラム）
- `ReservationConfirmedCommitted`:
  - status=Confirmed
- `ReservationCancelledCommitted`:
  - status=Cancelled
- `ReservationExpiredCommitted`:
  - status=Expired
- `ReservationRescheduledCommitted`:
  - start/end の更新（注意: 競合表示を避けたい場合は “新規Reservation作成”の方が現実的）

### 2.5 クエリ例
- `GET /rooms/{roomId}/calendar?from=...&to=...`
- `GET /reservations/{reservationId}`（詳細は ReservationDetailView を参照）

---

## 3. Read Model 2: ReservationDetailView（予約詳細）

### 3.1 目的
- 予約1件の詳細表示（編集画面、承認画面、キャンセル画面）
- DCB イベントを時系列で追えるようにする（監査/トレース）

### 3.2 形（推奨スキーマ）
**ReservationDetail**
- `reservationId: string`
- `roomId: string`
- `timeRange: { startUtc, endUtc }`
- `status: string`
- `title?: string`
- `description?: string`
- `organizerUserId: string`
- `attendeeCount?: number`
- `hasExternalGuests?: boolean`
- `requiredEquipment?: { [equipmentType: string]: number }`
- `equipmentPoolId?: string`
- `holdId?: string`
- `holdKind?: string`
- `holdExpiresAtUtc?: string`
- `approvalRequestId?: string`
- `approvalRequiredRole?: string`
- `approvalDeadlineUtc?: string`
- `approvalDecision?: "Approved" | "Rejected"`
- `approvalDecidedByUserId?: string`
- `approvalDecidedAtUtc?: string`
- `approvalRejectReason?: string`
- `confirmedAtUtc?: string`
- `cancelledAtUtc?: string`
- `cancelledByUserId?: string`
- `expiredAtUtc?: string`
- `lastEventId: string`
- `lastUpdatedAtUtc: string`

**ReservationEventTimelineItem**（任意だが実用感UP）
- `reservationId: string`
- `eventId: string`
- `eventType: string`
- `occurredAtUtc: string`
- `actorUserId: string`
- `summary: string`（短文）
- `correlationId?: string`

### 3.3 入力イベント
RoomCalendar と同じ + Timeline 用に全イベントを保存しても良い。

---

## 4. Read Model 3: ApprovalInboxView（承認待ち一覧）

### 4.1 目的
- FacilitiesApprover ロールのユーザーが「承認待ち」を一覧表示
- 予約内容（部屋、時間、主催者、理由）を一緒に出す

### 4.2 形（推奨スキーマ）
**ApprovalInboxItem**
- `approvalRequestId: string`
- `reservationId: string`
- `requiredRole: string`
- `deadlineUtc: string`
- `status: "Pending" | "Approved" | "Rejected" | "Expired" | "Cancelled"`
- `reasons: string[]`
- `requestedAtUtc: string`
- `requestedByUserId: string`（基本は organizer でも actor でもよい。サンプルでは actor を推奨）
- `roomId: string`
- `startUtc: string`
- `endUtc: string`
- `title?: string`
- `organizerUserId: string`
- `organizerDisplayName?: string`
- `decision?: "Approved" | "Rejected"`
- `decidedByUserId?: string`
- `decidedAtUtc?: string`
- `rejectReason?: string`
- `lastEventId: string`
- `lastUpdatedAtUtc: string`

### 4.3 入力イベント（DCB）
- `ApprovalFlowStarted`  
  - Item を作成、status=Pending
- `ApprovalDecisionRecorded`  
  - Approved/Rejected に更新
- `ReservationCancelledCommitted` / `ReservationExpiredCommitted`  
  - 対応する approvalRequestId があれば status=Cancelled/Expired に更新（payload の flags で判断）
- （任意）Room/Reservation更新イベントで表示情報を補完（タイトル等）

### 4.4 クエリ例
- `GET /approvals/inbox?requiredRole=FacilitiesApprover&status=Pending`

> 認可: 実際の API では actor が FacilitiesApprover ロールを持つかチェック。

---

## 5. Read Model 4: EquipmentScheduleView（設備の利用状況）

### 5.1 目的
- 時間帯ごとに「確保（Hold）」と「割当（Allocation）」を可視化
- 同時に残数（available）も出せると運用っぽい

### 5.2 形（推奨スキーマ）
**EquipmentScheduleSlot**
- `poolId: string`
- `equipmentType: string`
- `timeRange: TimeRange`
- `totalCount: number`
- `heldCount: number`
- `allocatedCount: number`
- `availableCount: number` (= total - held - allocated)
- `lastUpdatedAtUtc: string`

**EquipmentReservationLink**（詳細追跡用）
- `poolId: string`
- `equipmentType: string`
- `reservationId: string`
- `timeRange: TimeRange`
- `kind: "Hold" | "Allocation"`
- `holdKind?: HoldKind`
- `count: number`
- `holdId?: string`
- `createdAtUtc: string`
- `releasedAtUtc?: string`
- `lastEventId: string`

### 5.3 入力イベント（DCB）
- `EquipmentPoolCreated`（totalCount 初期化）
- `EquipmentStockAdjusted`（totalCount 更新）
- `ReservationHoldCommitted`（Succeeded のとき HoldLink/heldCount を増やす）
- `ReservationConfirmedCommitted`（AllocationLink/allocatedCount を増やし、releaseHold=true なら heldCount を減らす）
- `ReservationCancelledCommitted`（hold/allocation の解放を反映）
- `ReservationExpiredCommitted`（hold 解放を反映）

### 5.4 投影の注意点
- “時間帯”の集計は、最小サンプルでは **日単位/30分単位**に丸めて保持すると実装が簡単
- 厳密な任意時刻レンジの差分更新は複雑なので、サンプルでは丸めを明記してOK

---

## 6. Read Model 5: Directories（マスタ/ユーザー一覧）

### 6.1 RoomDirectory
**RoomDirectoryItem**
- `roomId, name, location, capacity, isActive, openHours?, alwaysRequiresApproval?, lastUpdatedAtUtc`

**入力イベント**
- `RoomRegistered`, `RoomUpdated`, `RoomDeactivated`, `RoomReactivated`

### 6.2 EquipmentDirectory
**EquipmentDirectoryItem**
- `poolId, location, stock: {type->count}, lastUpdatedAtUtc`

**入力イベント**
- `EquipmentPoolCreated`, `EquipmentStockAdjusted`

### 6.3 UserDirectoryList
**UserDirectoryItem**
- `userId, displayName, email?, department?, isActive, lastUpdatedAtUtc`

**入力イベント**
- `UserRegistered`, `UserProfileUpdated`, `UserDeactivated`, `UserReactivated`, `ExternalIdentityLinked/Unlinked`

### 6.4 UserAccessList
**UserAccessItem**
- `userId, roles: string[], isActive, lastUpdatedAtUtc`

**入力イベント**
- `UserAccessGranted`, `UserRoleGranted`, `UserRoleRevoked`, `UserAccessDeactivated`, `UserAccessReactivated`

---

## 7. Consistency Notes（DCB が Read Model に与える効果）

### 7.1 “矛盾のない一覧” が作りやすい
- `ReservationConfirmedCommitted` を受けた瞬間に、
  Reservation と EquipmentPool の双方に同じ確定事実が適用される前提のため、
  投影でも「確定だけ先に見える」などが起きにくい。

### 7.2 Query 側は “イベント=事実” を見ればよい
- Hold / Approval / Confirm / Cancel / Expire がすべて「Committed イベント」に統合されているため、
  Read Model はイベントタイプに応じた状態更新をすれば十分。
- サガの内部ステップ（補償、リトライ）に依存しない。

### 7.3 correlationId でトレースできる
- 予約確定に至る一連のイベントを correlationId で束ねると、
  UI から見た「操作の履歴」がそのまま監査ログになる。

---

## 8. Minimal API Mapping（参考：画面とRead Model）

- 部屋カレンダー: `RoomCalendarView`
- 予約詳細: `ReservationDetailView`
- 承認待ち: `ApprovalInboxView`
- 設備状況: `EquipmentScheduleView`
- マスタ/ユーザー: Directories

これらが揃うと、認証を足しただけで “社内アプリとして使える感” が出ます。