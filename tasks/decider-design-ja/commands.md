# commands.md

このドキュメントは「会議室予約＋設備＋承認（ロール承認）」サンプルのコマンド定義一覧です。
UI（社内アプリ）から呼ぶ API/UseCase に対応する粒度で設計します。

前提:
- 認証は外部未連携でも動くため、すべてのコマンドは `actorUserId` を受け取る（操作主体）。
- 予約の「主語（organizerUserId）」と「操作主体（actorUserId）」は分ける。
  - 例: 総務が代理で予約作成する / 管理者がキャンセルする
- 承認者はユーザー固定ではなく、`RequiredRole` を満たす任意ユーザーが承認可能。
- 時間帯は `TimeRange(startUtc, endUtc)`。

---

## Shared Types

### TimeRange
- `startUtc: string` (ISO-8601)
- `endUtc: string` (ISO-8601)

### EquipmentQuantities
- `requiredEquipment?: { [equipmentType: string]: number }`

### HoldKind
- `"Normal"` | `"Approval"`

---

# Room Commands (Aggregate: Room)

## RegisterRoom
**Purpose**: 会議室マスタ作成
- `roomId: string`
- `name: string`
- `location: string`
- `capacity: number`
- `openHours?: { startLocal: string, endLocal: string, timeZone: string }`
- `maxDurationMinutes?: number`
- `alwaysRequiresApproval?: boolean`
- `actorUserId: string`

## UpdateRoom
**Purpose**: 会議室マスタ更新
- `roomId: string`
- `patch: {
    name?: string,
    location?: string,
    capacity?: number,
    openHours?: { startLocal: string, endLocal: string, timeZone: string },
    maxDurationMinutes?: number,
    alwaysRequiresApproval?: boolean
  }`
- `actorUserId: string`

## DeactivateRoom
- `roomId: string`
- `reason?: string`
- `actorUserId: string`

## ReactivateRoom
- `roomId: string`
- `actorUserId: string`

---

# EquipmentPool Commands (Aggregate: EquipmentPool)

## CreateEquipmentPool
**Purpose**: 拠点設備プール作成
- `poolId: string`
- `location: string`
- `initialStock: { [equipmentType: string]: number }`
- `actorUserId: string`

## AdjustEquipmentStock
**Purpose**: 在庫増減（購入/廃棄/故障）
- `poolId: string`
- `adjustments: { [equipmentType: string]: number }`
- `reason: string`
- `actorUserId: string`

## PlaceEquipmentHold
**Purpose**: 設備の仮確保（在庫を時間帯で押さえる）
- `poolId: string`
- `holdId: string`
- `reservationId: string`
- `timeRange: TimeRange`
- `requested: { [equipmentType: string]: number }`
- `holdKind: HoldKind`
- `expiresAtUtc: string`
- `actorUserId: string`

## ReleaseEquipmentHold
**Purpose**: 仮確保の解放（キャンセル/期限切れ/ロールバック）
- `poolId: string`
- `holdId: string`
- `reservationId: string`
- `reason: string`
- `actorUserId: string`

## AllocateEquipment
**Purpose**: 予約確定に伴う割当確定（通常は Hold → Allocate）
- `poolId: string`
- `reservationId: string`
- `timeRange: TimeRange`
- `requested: { [equipmentType: string]: number }`
- `sourceHoldId?: string`
- `actorUserId: string`

## ReleaseEquipmentAllocation
**Purpose**: 確定割当の解放（予約キャンセル）
- `poolId: string`
- `reservationId: string`
- `reason: string`
- `actorUserId: string`

---

# Reservation Commands (Aggregate: Reservation)

## CreateReservationDraft
**Purpose**: 予約下書き作成（最初の1手）
- `reservationId: string`
- `roomId: string`
- `timeRange: TimeRange`
- `organizerUserId: string`
- `title?: string`
- `actorUserId: string`

## UpdateReservationDetails
**Purpose**: 予約詳細更新（人数/説明/外部参加/設備オプション）
- `reservationId: string`
- `patch: {
    title?: string,
    description?: string,
    attendeeCount?: number,
    hasExternalGuests?: boolean,
    requiredEquipment?: { [equipmentType: string]: number }
  }`
- `actorUserId: string`

## RequestReservationHold
**Purpose**: 「仮確保フェーズ開始」(プロセス起動のトリガ)
- `reservationId: string`
- `actorUserId: string`

## MarkReservationHoldSucceeded
**Purpose**: プロセス側が Reservation を Held に遷移させる（外部調停用）
- `reservationId: string`
- `equipmentHoldId?: string`
- `equipmentHoldKind?: HoldKind`
- `equipmentHoldExpiresAtUtc?: string`
- `actorUserId: string`

## MarkReservationHoldFailed
**Purpose**: 仮確保失敗を反映
- `reservationId: string`
- `reason: string`
- `actorUserId: string`

## DetermineApprovalRequirement
**Purpose**: 承認要否と理由を確定（Room/Option/Equipmentなどのポリシー結果を反映）
- `reservationId: string`
- `approvalRequired: boolean`
- `reasons: string[]`
- `actorUserId: string`

## SubmitReservationForApproval
**Purpose**: 承認依頼を作成し PendingApproval へ
- `reservationId: string`
- `approvalRequestId: string`
- `requiredRole: string` (例: "FacilitiesApprover")
- `deadlineUtc: string`
- `reasons: string[]`
- `actorUserId: string`

## MarkReservationApproved
**Purpose**: 承認結果を Reservation 側へ反映（ApprovedByを記録）
- `reservationId: string`
- `approvalRequestId: string`
- `approvedByUserId: string`
- `approvedAtUtc: string`
- `actorUserId: string`

## MarkReservationRejected
**Purpose**: 却下結果を Reservation 側へ反映
- `reservationId: string`
- `approvalRequestId: string`
- `rejectedByUserId: string`
- `rejectedAtUtc: string`
- `reason: string`
- `actorUserId: string`

## MarkReservationApprovalExpired
**Purpose**: 承認期限切れを Reservation 側へ反映
- `reservationId: string`
- `approvalRequestId: string`
- `expiredAtUtc: string`
- `actorUserId: string`

## ConfirmReservation
**Purpose**: 最終確定（設備割当が完了することが前提/またはここで実行）
- `reservationId: string`
- `actorUserId: string`

## MarkReservationConfirmationFailed
**Purpose**: 確定失敗を反映
- `reservationId: string`
- `reason: string`
- `actorUserId: string`

## CancelReservation
**Purpose**: キャンセル（Held/Pending/Confirmed いずれも可にすると現実的）
- `reservationId: string`
- `reason?: string`
- `actorUserId: string`

## ExpireReservation
**Purpose**: 自動失効（Hold期限切れ、承認期限切れなど）
- `reservationId: string`
- `expiredAtUtc: string`
- `reason: string` (例: "HoldExpired" | "ApprovalExpired")
- `actorUserId: string` (system user でもよい)

## RescheduleReservation (optional)
**Purpose**: 日時変更（実務的には「新規Hold→承認→Confirm→旧解放」でもOK）
- `reservationId: string`
- `newTimeRange: TimeRange`
- `actorUserId: string`

---

# ApprovalRequest Commands (Aggregate: ApprovalRequest)

## CreateApprovalRequest
**Purpose**: 承認依頼を作成
- `approvalRequestId: string`
- `targetType: "Reservation"`
- `targetId: string` (reservationId)
- `requestedByUserId: string`
- `requiredRole: string` (例: "FacilitiesApprover")
- `deadlineUtc: string`
- `reasons: string[]`
- `note?: string`
- `actorUserId: string`

## ApproveRequest
**Purpose**: 承認（実行者が requiredRole を持つこと）
- `approvalRequestId: string`
- `approvedByUserId: string` (通常 actorUserId と同一)
- `approvedAtUtc: string`
- `note?: string`
- `actorUserId: string`

## RejectRequest
**Purpose**: 却下（実行者が requiredRole を持つこと）
- `approvalRequestId: string`
- `rejectedByUserId: string` (通常 actorUserId と同一)
- `rejectedAtUtc: string`
- `reason: string`
- `note?: string`
- `actorUserId: string`

## ExpireApprovalRequest
**Purpose**: 期限切れ処理（スケジューラ/バッチ等）
- `approvalRequestId: string`
- `expiredAtUtc: string`
- `actorUserId: string` (system)

## CancelApprovalRequest
**Purpose**: 取り下げ（予約キャンセル等に連動）
- `approvalRequestId: string`
- `cancelledByUserId: string`
- `cancelledAtUtc: string`
- `reason: string`
- `actorUserId: string`

---

# UserDirectory Commands (Aggregate: UserDirectory)

## RegisterUser
**Purpose**: ユーザー台帳に追加（認証未連携用）
- `userId: string`
- `displayName: string`
- `email?: string`
- `department?: string`
- `actorUserId: string`

## UpdateUserProfile
- `userId: string`
- `patch: {
    displayName?: string,
    email?: string,
    department?: string
  }`
- `actorUserId: string`

## DeactivateUser
- `userId: string`
- `reason?: string`
- `actorUserId: string`

## ReactivateUser
- `userId: string`
- `actorUserId: string`

## LinkExternalIdentity (optional)
**Purpose**: 認証基盤導入後に外部ID紐付け
- `userId: string`
- `provider: string`
- `subjectId: string`
- `actorUserId: string`

## UnlinkExternalIdentity (optional)
- `userId: string`
- `provider: string`
- `subjectId: string`
- `actorUserId: string`

---

# UserAccess Commands (Aggregate: UserAccess)

## GrantUserAccess
**Purpose**: 権限レコード作成/初期ロール付与
- `userId: string`
- `initialRoles: string[]`
- `actorUserId: string`

## GrantRole
- `userId: string`
- `role: string`
- `reason?: string`
- `actorUserId: string`

## RevokeRole
- `userId: string`
- `role: string`
- `reason?: string`
- `actorUserId: string`

## DeactivateUserAccess
- `userId: string`
- `reason?: string`
- `actorUserId: string`

## ReactivateUserAccess
- `userId: string`
- `actorUserId: string`

---

## Notes (Implementation Hints)

- `Mark*` 系のコマンドは「プロセス（Saga/Policy/Orchestrator）」が結果を反映するためのもの。
  フレームワークのサンプルとして “複数集約の一貫性” を見せやすい。
- より単純にするなら `Reservation` 側で外部結果の反映を `ConfirmReservation` の内部に吸収し、
  `Mark*` を減らしてもよい（ただしサンプルの見せ場は減る）。

必要なら次は policies_sagas.md（Reservation↔EquipmentPool↔ApprovalRequest の連携手順、失敗時ロールバック、期限切れ処理）を書きます。これが揃うと実装がほぼ一本道になります。