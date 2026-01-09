# events.md (DCB version)

このドキュメントは「会議室予約＋設備＋承認（ロール承認）」サンプルの **DCB（Dynamic Consistency Boundary）前提**のイベント定義です。

DCB 前提では、**“1つの事実” を 1イベントとして記録し、そのイベントを複数集約にタグ付け**して、
フレームワークが複数集約の一貫性を同一境界として扱えるようにします。

> つまり、「ReservationHoldSucceeded と EquipmentHoldPlaced」などの“同じ事実の二重記録”は不要になり、
> 1イベントに統合します（ただし運用上「試行ログ」等が必要なら別イベントにしてもOK）。

---

## 0. Event Envelope（共通）

すべてのイベントは以下の共通メタデータを持つ想定です。

- `eventId: string` (ULID/UUID)
- `eventType: string`
- `occurredAtUtc: string` (ISO-8601)
- `actorUserId: string` (system の場合は `"system"`)
- `correlationId?: string` (一連の操作・リクエスト単位)
- `causationId?: string` (直前の原因イベントがある場合)
- `tags: AggregateTag[]` (**DCBの核：このイベントが紐づく集約一覧**)
- `payload: object`

### AggregateTag
- `aggregateType: string` 例: `"Room" | "Reservation" | "EquipmentPool" | "ApprovalRequest" | "UserDirectory" | "UserAccess"`
- `aggregateId: string`
- `role?: string` 例: `"primary" | "secondary"`（任意。読者にわかりやすくするため）

---

## 1. Shared Types（payloadで使う型）

### TimeRange
- `startUtc: string`
- `endUtc: string`

### EquipmentQuantities
- `items?: { [equipmentType: string]: number }`
  - 例: `{ "Projector": 1, "Mic": 2 }`

### HoldKind
- `"Normal"`: 通常の確定フロー用（短TTL）
- `"Approval"`: 承認待ち用（長TTL）

### ApprovalDecision
- `"Approved" | "Rejected"`

---

# 2. Master / Admin Events（単一集約）

## 2.1 Room

### RoomRegistered
**tags**
- `[ { aggregateType: "Room", aggregateId: roomId, role: "primary" } ]`
**payload**
- `roomId: string`
- `name: string`
- `location: string` (例: "HQ-7F")
- `capacity: number`
- `isActive: boolean`
- `openHours?: { startLocal: string, endLocal: string, timeZone: string }`
- `maxDurationMinutes?: number`
- `alwaysRequiresApproval?: boolean`

### RoomUpdated
**tags**: Room
**payload**
- `roomId: string`
- `patch: {
    name?: string,
    location?: string,
    capacity?: number,
    isActive?: boolean,
    openHours?: { startLocal: string, endLocal: string, timeZone: string },
    maxDurationMinutes?: number,
    alwaysRequiresApproval?: boolean
  }`

### RoomDeactivated
**tags**: Room
**payload**
- `roomId: string`
- `reason?: string`

### RoomReactivated
**tags**: Room
**payload**
- `roomId: string`

---

## 2.2 EquipmentPool

### EquipmentPoolCreated
**tags**
- `[ { aggregateType: "EquipmentPool", aggregateId: poolId, role: "primary" } ]`
**payload**
- `poolId: string`
- `location: string`
- `initialStock: { [equipmentType: string]: number }`

### EquipmentStockAdjusted
**tags**: EquipmentPool
**payload**
- `poolId: string`
- `adjustments: { [equipmentType: string]: number }`
- `reason: string`

---

## 2.3 UserDirectory（名簿）

### UserRegistered
**tags**
- `[ { aggregateType: "UserDirectory", aggregateId: userId, role: "primary" } ]`
**payload**
- `userId: string`
- `displayName: string`
- `email?: string`
- `department?: string`
- `isActive: boolean`

### UserProfileUpdated
**tags**: UserDirectory
**payload**
- `userId: string`
- `patch: { displayName?: string, email?: string, department?: string }`

### UserDeactivated
**tags**: UserDirectory
**payload**
- `userId: string`
- `reason?: string`

### UserReactivated
**tags**: UserDirectory
**payload**
- `userId: string`

### ExternalIdentityLinked (optional)
**tags**: UserDirectory
**payload**
- `userId: string`
- `provider: string` (例: "EntraId" | "Keycloak")
- `subjectId: string`

### ExternalIdentityUnlinked (optional)
**tags**: UserDirectory
**payload**
- `userId: string`
- `provider: string`
- `subjectId: string`

---

## 2.4 UserAccess（権限）

### UserAccessGranted
**tags**
- `[ { aggregateType: "UserAccess", aggregateId: userId, role: "primary" } ]`
**payload**
- `userId: string`
- `initialRoles: string[]`

### UserRoleGranted
**tags**: UserAccess
**payload**
- `userId: string`
- `role: string`
- `reason?: string`

### UserRoleRevoked
**tags**: UserAccess
**payload**
- `userId: string`
- `role: string`
- `reason?: string`

### UserAccessDeactivated
**tags**: UserAccess
**payload**
- `userId: string`
- `reason?: string`

### UserAccessReactivated
**tags**: UserAccess
**payload**
- `userId: string`

---

# 3. Reservation / Approval / Equipment DCB Events（複数集約）

以降のイベントは **“1つの事実” を複数集約にタグ付け**します。
これにより、フレームワークは「同一イベントに紐づく集約更新」を **同じ整合性境界**として扱えます。

---

## 3.1 Reservation Draft & Update（主に Reservation 単体）

### ReservationDraftCreated
**tags**
- `[ { aggregateType: "Reservation", aggregateId: reservationId, role: "primary" } ]`
**payload**
- `reservationId: string`
- `roomId: string`
- `timeRange: TimeRange`
- `organizerUserId: string`
- `title?: string`

### ReservationDetailsUpdated
**tags**: Reservation
**payload**
- `reservationId: string`
- `patch: {
    title?: string,
    description?: string,
    attendeeCount?: number,
    hasExternalGuests?: boolean,
    requiredEquipment?: { [equipmentType: string]: number }
  }`

---

## 3.2 Hold（設備の仮確保 + ReservationのHeld化 を 1イベントに統合）

### ReservationHoldCommitted
**What this fact means**
- 「この予約は、（必要なら）設備を仮確保できたため Held になった」
- または「仮確保に失敗したため Held になれなかった」

**tags（成功時）**
- `{ Reservation(reservationId) }`
- `{ EquipmentPool(poolId) }`
**tags（失敗時の方針）**
- 最小サンプルでは Reservation のみにしてOK（設備側の状態が変わらないため）
- “試行ログも残す” 方針なら EquipmentPool にもタグ付けしてOK

**payload（共通）**
- `reservationId: string`
- `roomId: string`
- `poolId: string`
- `timeRange: TimeRange`
- `requestedEquipment?: { [equipmentType: string]: number }`
- `result: "Succeeded" | "Failed"`
- `failureReason?: string`

**payload（Succeeded のとき）**
- `holdId: string`
- `holdKind: HoldKind`
- `expiresAtUtc: string`

> ここで従来の `EquipmentHoldPlaced` + `ReservationHoldSucceeded` を統合します。
> 事実は1つで、タグが複数、という形に寄せます。

---

## 3.3 Approval Request Creation（承認依頼作成 + ReservationのPending化 を 1イベントに統合）

### ApprovalFlowStarted
**What this fact means**
- 「この予約は承認が必要と判定され、承認依頼が開始された」
- Reservation は `PendingApproval` へ
- ApprovalRequest は `Pending` で生成される

**tags**
- `{ Reservation(reservationId) }`
- `{ ApprovalRequest(approvalRequestId) }`

**payload**
- `reservationId: string`
- `approvalRequestId: string`
- `requiredRole: string` (例: `"FacilitiesApprover"`)
- `deadlineUtc: string`
- `reasons: string[]` (例: `"OutsideBusinessHours"`, `"ExternalGuests"`, `"HighCostEquipment"`)
- `note?: string`

> ここで従来の
> - `ReservationApprovalRequiredDetermined`
> - `ApprovalRequested`
> - `ReservationSubmittedForApproval`
> を **1つの開始事実**に統合しています。
> （承認要否の“判定だけ”を監査したい場合は別イベントに分けてもOK）

---

## 3.4 Approval Decision（承認/却下の結果を 1イベントに統合）

### ApprovalDecisionRecorded
**What this fact means**
- 「承認依頼に対して、承認（または却下）が確定した」
- ApprovalRequest 側と Reservation 側が同じ決定を持つ

**tags**
- `{ ApprovalRequest(approvalRequestId) }`
- `{ Reservation(reservationId) }`

**payload**
- `approvalRequestId: string`
- `reservationId: string`
- `decision: ApprovalDecision` (`"Approved"` | `"Rejected"`)
- `decidedByUserId: string`
- `decidedAtUtc: string`
- `reason?: string` (Rejected の場合は必須推奨)
- `note?: string`

---

## 3.5 Confirm（確定 + 設備割当確定 を 1イベントに統合）

### ReservationConfirmedCommitted
**What this fact means**
- 「予約が確定し、必要な設備割当も確定した」
- “確定”は Reservation と EquipmentPool の双方の状態が一致して初めて成立する

**tags**
- `{ Reservation(reservationId) }`
- `{ EquipmentPool(poolId) }`

**payload**
- `reservationId: string`
- `roomId: string`
- `poolId: string`
- `timeRange: TimeRange`
- `confirmedAtUtc: string`

**payload（設備がある場合）**
- `allocatedEquipment?: { [equipmentType: string]: number }`
- `sourceHoldId?: string`
- `releaseHold?: boolean` (通常 true)

> ここで従来の `EquipmentAllocated` + `ReservationConfirmed` を統合します。

---

## 3.6 Cancel（キャンセル + 設備解放 + 承認取り下げを 1イベントに統合）

### ReservationCancelledCommitted
**What this fact means**
- 「予約がキャンセルされた」
- 状況に応じて
  - hold を解放する
  - allocation を解放する
  - 承認依頼を取り下げる
  を **同一事実**として扱う（部分キャンセルを作らない）

**tags（状況に応じて複数）**
- `{ Reservation(reservationId) }` は常に
- `{ EquipmentPool(poolId) }` は設備を hold/allocation している場合
- `{ ApprovalRequest(approvalRequestId) }` は承認フロー中の場合

**payload**
- `reservationId: string`
- `cancelledByUserId: string`
- `cancelledAtUtc: string`
- `reason?: string`

**payload（設備関連：必要なものだけ入れる）**
- `poolId?: string`
- `holdId?: string`
- `allocationReleased?: boolean`

**payload（承認関連）**
- `approvalRequestId?: string`
- `approvalCancelled?: boolean`

---

## 3.7 Expire / Timeout（期限切れ + 解放 を 1イベントに統合）

### ReservationExpiredCommitted
**What this fact means**
- 「予約が期限切れで失効した」
- 状況に応じて hold の解放、承認期限切れを同時に刻む

**tags（状況に応じて複数）**
- `{ Reservation(reservationId) }` は常に
- `{ EquipmentPool(poolId) }` は hold を解放する場合
- `{ ApprovalRequest(approvalRequestId) }` は承認期限切れの場合

**payload**
- `reservationId: string`
- `expiredAtUtc: string`
- `reason: "HoldExpired" | "ApprovalExpired"`
- `actorUserId: string` (通常 "system")

**payload（設備関連）**
- `poolId?: string`
- `holdId?: string`

**payload（承認関連）**
- `approvalRequestId?: string`

---

## 3.8 Reschedule（任意：日時変更を “事実1つ” として表す）

### ReservationRescheduledCommitted (optional)
**What this fact means**
- 「予約の時間帯が変更された」
- 実務的には “新規Hold→（必要なら承認）→Confirm→旧解放” の一連でやるのが安全
- サンプル簡略化のために 1イベントにまとめる場合の選択肢

**tags**
- `{ Reservation(reservationId) }`
- `{ EquipmentPool(poolId) }`（設備を伴うなら）

**payload**
- `reservationId: string`
- `poolId?: string`
- `oldTimeRange: TimeRange`
- `newTimeRange: TimeRange`
- `actorUserId: string`

> おすすめ運用（実装を簡単にしつつ現実的）
> - Reschedule は “複合操作” として扱い、上の Committed 系イベントを順に発行する（DCBで部分成功を避ける）
> - この optional は「サンプルをさらに短くしたい」場合のみ

---

# 4. まとめ：DCB版で “統合されたイベント” 一覧

従来の「集約ごとに同じ事実を別イベントで二重記録」していた箇所を、以下に統合しました。

- Hold 成否
  - `ReservationHoldCommitted` （Reservation + EquipmentPool）
- 承認開始
  - `ApprovalFlowStarted` （Reservation + ApprovalRequest）
- 承認結果
  - `ApprovalDecisionRecorded` （ApprovalRequest + Reservation）
- 確定
  - `ReservationConfirmedCommitted` （Reservation + EquipmentPool）
- キャンセル
  - `ReservationCancelledCommitted` （Reservation + EquipmentPool (+ApprovalRequest)）
- 期限切れ
  - `ReservationExpiredCommitted` （Reservation + EquipmentPool (+ApprovalRequest)）

この形により、フレームワークは **イベント単位で一貫した事実**を複数集約へ適用でき、
「予約だけ進んだ」「設備だけ確保された」といった中間状態を設計の中心課題から外せます。

次は readmodels.md へ行けますが、DCB の価値を読者に伝えるには **「タグが複数のイベントをどう投影するか」**が肝になります。
readmodels.md では、(1) 予約カレンダー、(2) 承認待ち一覧、(3) 設備の時間帯在庫ビュー を、DCBイベントから矛盾なく作れる形で書き起こします。