以下が events.md のたたき台です（そのままリポジトリに置ける体裁）。
※ “承認はロールで誰でも可能” 前提で、ApprovalRequest 側は RequiredRole を保持します。

# events.md

このドキュメントは「会議室予約＋設備＋承認（ロール承認）」サンプルのイベント定義一覧です。

- 認証は外部（未連携でも動作）を想定し、すべてのイベントは `actorUserId` を持てる前提。
- 承認者はユーザー固定ではなく、`RequiredRole` を満たす任意ユーザーが承認可能。
- 時間帯は `TimeRange`（start/end）で表現する。

---

## Shared Types

### TimeRange
- `startUtc: string` (ISO-8601)
- `endUtc: string` (ISO-8601)

### EquipmentType
文字列で扱う（例: `"Projector"`, `"Mic"`, `"Speaker"`, `"Whiteboard"`）

### EquipmentQuantities
- `items: { [equipmentType: string]: number }`

### HoldKind
- `"Normal"`: 通常の確定フロー用（短TTL想定）
- `"Approval"`: 承認待ち用（長TTL想定）

---

# Room Events

## RoomRegistered
**Aggregate**: Room  
**When**: 部屋マスタ作成
- `roomId: string`
- `name: string`
- `location: string` (例: "HQ-7F")
- `capacity: number`
- `isActive: boolean`
- `openHours?: { startLocal: string, endLocal: string, timeZone: string }`
- `maxDurationMinutes?: number`
- `alwaysRequiresApproval?: boolean`
- `actorUserId: string`
- `occurredAtUtc: string`

## RoomUpdated
**Aggregate**: Room  
**When**: 名称/収容人数/場所などの更新
- `roomId: string`
- `patch: {
    name?: string,
    location?: string,
    capacity?: number,
    isActive?: boolean
  }`
- `actorUserId: string`
- `occurredAtUtc: string`

## RoomDeactivated
**Aggregate**: Room  
**When**: 利用停止
- `roomId: string`
- `reason?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## RoomReactivated
**Aggregate**: Room  
**When**: 利用再開
- `roomId: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## RoomPolicyUpdated (optional)
**Aggregate**: Room  
**When**: ルール（営業時間、最大予約時間、承認条件など）更新を分離したい場合
- `roomId: string`
- `policy: {
    openHours?: { startLocal: string, endLocal: string, timeZone: string },
    maxDurationMinutes?: number,
    alwaysRequiresApproval?: boolean
  }`
- `actorUserId: string`
- `occurredAtUtc: string`

---

# EquipmentPool Events

## EquipmentPoolCreated
**Aggregate**: EquipmentPool  
**When**: 拠点単位の設備プール作成
- `poolId: string`
- `location: string`
- `initialStock: { [equipmentType: string]: number }`
- `actorUserId: string`
- `occurredAtUtc: string`

## EquipmentStockAdjusted
**Aggregate**: EquipmentPool  
**When**: 在庫増減（購入・廃棄・故障など）
- `poolId: string`
- `adjustments: { [equipmentType: string]: number }` (増減。例: Projector -1)
- `reason: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## EquipmentHoldPlaced
**Aggregate**: EquipmentPool  
**When**: 時間帯に対する設備の仮確保に成功
- `poolId: string`
- `holdId: string`
- `reservationId: string`
- `timeRange: TimeRange`
- `requested: EquipmentQuantities`
- `holdKind: HoldKind`
- `expiresAtUtc: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## EquipmentHoldRejected
**Aggregate**: EquipmentPool  
**When**: 仮確保に失敗（在庫不足/ルール違反など）
- `poolId: string`
- `reservationId: string`
- `timeRange: TimeRange`
- `requested: EquipmentQuantities`
- `reason: string` (例: "InsufficientStock:Projector")
- `actorUserId: string`
- `occurredAtUtc: string`

## EquipmentHoldReleased
**Aggregate**: EquipmentPool  
**When**: 仮確保の解放（キャンセル、期限切れ、ロールバック）
- `poolId: string`
- `holdId: string`
- `reservationId: string`
- `reason: string` (例: "Cancelled" | "Expired" | "Rollback")
- `actorUserId: string`
- `occurredAtUtc: string`

## EquipmentAllocated
**Aggregate**: EquipmentPool  
**When**: 予約確定に伴い割当を確定
- `poolId: string`
- `reservationId: string`
- `timeRange: TimeRange`
- `allocated: EquipmentQuantities`
- `sourceHoldId?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## EquipmentAllocationFailed (optional)
**Aggregate**: EquipmentPool  
**When**: 確定割当が失敗（例示。原則起こらない設計が理想）
- `poolId: string`
- `reservationId: string`
- `timeRange: TimeRange`
- `requested: EquipmentQuantities`
- `reason: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## EquipmentAllocationReleased
**Aggregate**: EquipmentPool  
**When**: 確定後の解放（予約キャンセル）
- `poolId: string`
- `reservationId: string`
- `reason: string` (例: "ReservationCancelled")
- `actorUserId: string`
- `occurredAtUtc: string`

---

# Reservation Events

## ReservationDraftCreated
**Aggregate**: Reservation  
**When**: 予約下書き作成
- `reservationId: string`
- `roomId: string`
- `timeRange: TimeRange`
- `organizerUserId: string`
- `title?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationDetailsUpdated
**Aggregate**: Reservation  
**When**: 予約詳細更新（人数、説明、設備オプション等）
- `reservationId: string`
- `patch: {
    title?: string,
    description?: string,
    attendeeCount?: number,
    hasExternalGuests?: boolean,
    requiredEquipment?: { [equipmentType: string]: number }
  }`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationHoldRequested
**Aggregate**: Reservation  
**When**: 仮確保フェーズ開始（UI操作/プロセス開始）
- `reservationId: string`
- `requestedEquipment?: { [equipmentType: string]: number }`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationHoldSucceeded
**Aggregate**: Reservation  
**When**: 仮確保に成功し、Held状態になった
- `reservationId: string`
- `equipmentHoldId?: string`
- `equipmentHoldKind?: HoldKind`
- `equipmentHoldExpiresAtUtc?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationHoldFailed
**Aggregate**: Reservation  
**When**: 仮確保失敗
- `reservationId: string`
- `reason: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationApprovalRequiredDetermined
**Aggregate**: Reservation  
**When**: 承認要否と理由が決定
- `reservationId: string`
- `approvalRequired: boolean`
- `reasons: string[]` (例: "OutsideBusinessHours", "ExternalGuests", "HighCostEquipment")
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationSubmittedForApproval
**Aggregate**: Reservation  
**When**: 承認依頼を作成し、PendingApprovalへ
- `reservationId: string`
- `approvalRequestId: string`
- `requiredRole: string` (例: "FacilitiesApprover")
- `deadlineUtc: string`
- `reasons: string[]`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationApprovalGranted
**Aggregate**: Reservation  
**When**: 承認済み（Approved状態をReservation側にも反映）
- `reservationId: string`
- `approvalRequestId: string`
- `approvedByUserId: string`
- `approvedAtUtc: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationApprovalRejected
**Aggregate**: Reservation  
**When**: 却下
- `reservationId: string`
- `approvalRequestId: string`
- `rejectedByUserId: string`
- `rejectedAtUtc: string`
- `reason: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationApprovalExpired
**Aggregate**: Reservation  
**When**: 承認期限切れ
- `reservationId: string`
- `approvalRequestId: string`
- `expiredAtUtc: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationConfirmed
**Aggregate**: Reservation  
**When**: 最終確定（設備割当も完了している前提）
- `reservationId: string`
- `confirmedAtUtc: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationConfirmationFailed
**Aggregate**: Reservation  
**When**: 確定処理に失敗
- `reservationId: string`
- `reason: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationCancelled
**Aggregate**: Reservation  
**When**: キャンセル
- `reservationId: string`
- `cancelledByUserId: string`
- `cancelledAtUtc: string`
- `reason?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationExpired
**Aggregate**: Reservation  
**When**: Heldの期限切れ等で自動失効
- `reservationId: string`
- `expiredAtUtc: string`
- `reason: string` (例: "HoldExpired" | "ApprovalExpired")
- `actorUserId: string`
- `occurredAtUtc: string`

## ReservationRescheduled (optional)
**Aggregate**: Reservation  
**When**: 日時変更をイベント1つで表現する場合
- `reservationId: string`
- `oldTimeRange: TimeRange`
- `newTimeRange: TimeRange`
- `actorUserId: string`
- `occurredAtUtc: string`

---

# ApprovalRequest Events

## ApprovalRequested
**Aggregate**: ApprovalRequest  
**When**: 承認依頼作成
- `approvalRequestId: string`
- `targetType: "Reservation"`
- `targetId: string` (reservationId)
- `requestedByUserId: string`
- `requiredRole: string` (例: "FacilitiesApprover")
- `deadlineUtc: string`
- `reasons: string[]`
- `note?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ApprovalGranted
**Aggregate**: ApprovalRequest  
**When**: 承認（ロールを満たす任意ユーザー）
- `approvalRequestId: string`
- `approvedByUserId: string`
- `approvedAtUtc: string`
- `note?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ApprovalRejected
**Aggregate**: ApprovalRequest  
**When**: 却下
- `approvalRequestId: string`
- `rejectedByUserId: string`
- `rejectedAtUtc: string`
- `reason: string`
- `note?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ApprovalExpired
**Aggregate**: ApprovalRequest  
**When**: 期限切れ
- `approvalRequestId: string`
- `expiredAtUtc: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ApprovalCancelled
**Aggregate**: ApprovalRequest  
**When**: 依頼取り下げ（予約側キャンセル等）
- `approvalRequestId: string`
- `cancelledByUserId: string`
- `cancelledAtUtc: string`
- `reason: string`
- `actorUserId: string`
- `occurredAtUtc: string`

---

# UserDirectory Events

## UserRegistered
**Aggregate**: UserDirectory  
**When**: ユーザー台帳に登録
- `userId: string`
- `displayName: string`
- `email?: string`
- `department?: string`
- `isActive: boolean`
- `actorUserId: string`
- `occurredAtUtc: string`

## UserProfileUpdated
**Aggregate**: UserDirectory  
**When**: 表示名/メール/所属の更新
- `userId: string`
- `patch: {
    displayName?: string,
    email?: string,
    department?: string
  }`
- `actorUserId: string`
- `occurredAtUtc: string`

## UserDeactivated
- `userId: string`
- `reason?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## UserReactivated
- `userId: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ExternalIdentityLinked (optional)
**When**: 認証基盤導入後に外部IDを紐付け
- `userId: string`
- `provider: string` (例: "EntraId" | "Keycloak")
- `subjectId: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## ExternalIdentityUnlinked (optional)
- `userId: string`
- `provider: string`
- `subjectId: string`
- `actorUserId: string`
- `occurredAtUtc: string`

---

# UserAccess Events

## UserAccessGranted
**Aggregate**: UserAccess  
**When**: 権限レコード作成/有効化
- `userId: string`
- `initialRoles: string[]` (例: ["Employee"])
- `actorUserId: string`
- `occurredAtUtc: string`

## UserRoleGranted
- `userId: string`
- `role: string`
- `reason?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## UserRoleRevoked
- `userId: string`
- `role: string`
- `reason?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## UserAccessDeactivated
- `userId: string`
- `reason?: string`
- `actorUserId: string`
- `occurredAtUtc: string`

## UserAccessReactivated
- `userId: string`
- `actorUserId: string`
- `occurredAtUtc: string`

次は commands.md を作ると、UI/APIがそのまま実装できます（Draft作成、Hold、SubmitForApproval、Approve、Confirm、Cancel など）。必要なら続けて書きます。

