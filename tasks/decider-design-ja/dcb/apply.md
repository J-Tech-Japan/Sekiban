# apply.md (DCB + 状態型DU: 状態遷移表)

このドキュメントは DCB 版イベント（`tags_and_events.md`）を、各 TagState（AggregateState）に **Apply** したときの
状態遷移を「型（Discriminated Union）」で表現するための一覧です。

目的:
- null を見ない
- “現在の状態型” と “イベント型” の組み合わせで許可/不許可を決定する
- 不正な遷移は Apply で弾く（例外/リジェクト）ことで一貫性を守る

---

## 0. ルール（共通）

- Apply は **純粋関数**（`newState = Apply(oldState, @event)`）として扱う。
- DCB イベントは tags により複数集約に紐付くため、**同じイベントが複数の Apply に入る**。
- ただし各集約は「自分に関係する payload 部分」だけを使い、状態更新する。
- 遷移表にない組み合わせは原則 **InvalidTransition**（適用不可）。

---

## 1. ReservationState 遷移表

### 1.1 ReservationState（代表）
- `Draft`
- `Held`
- `PendingApproval`
- `ApprovedAwaitingConfirm`
- `Confirmed`
- `Rejected`
- `Cancelled`
- `Expired`

---

### 1.2 Events → ReservationState

#### Event: ReservationDraftCreated
**From**
- `null / NotExists`
**To**
- `Draft`
**Notes**
- reservationId, roomId, timeRange, organizerUserId を確定
- Details は空（既定値）で初期化

---

#### Event: ReservationDetailsUpdated
**From**
- `Draft`
- `Held`
- `PendingApproval`
- `ApprovedAwaitingConfirm`
**To**
- 同じ状態型のまま（中身更新）
**Invalid**
- `Confirmed`, `Rejected`, `Cancelled`, `Expired`
**Notes**
- patch 適用
- requiredEquipment 変更を許すかはサンプル方針次第  
  - 許す場合: 変更後の整合性は次の Hold/Confirm で評価  
  - 厳密にする場合: Held 以降で requiredEquipment 変更は不可

---

#### Event: ReservationHoldCommitted
payload: `result = Succeeded | Failed`

**From**
- `Draft`
**To**
- `Held`（Succeeded）
- `Draft`（Failed：状態維持、もしくはエラーフィールドを Draft の内部に保持してもよい）
**Invalid**
- `Held` 以降すべて（再Hold禁止にするなら）
**Notes**
- Succeeded のとき `Hold`（poolId, holdId, holdKind, expiresAtUtc）を確定
- Failed のときは ReadModel に失敗理由を残す（状態型に入れない方がシンプル）

---

#### Event: ApprovalFlowStarted
**From**
- `Held`
**To**
- `PendingApproval`
**Invalid**
- `Draft`, `PendingApproval` 以降すべて
**Notes**
- `ApprovalRef(approvalRequestId, requiredRole, deadlineUtc, reasons)` を保持
- holdKind が Approval に延長される設計なら、Hold の expiresAt 更新もここで行う（オプション）

---

#### Event: ApprovalDecisionRecorded
payload: `decision = Approved | Rejected`

**From**
- `PendingApproval`
**To**
- `ApprovedAwaitingConfirm`（Approved）
- `Rejected`（Rejected）
**Invalid**
- `Draft`, `Held`, `ApprovedAwaitingConfirm`, `Confirmed`, `Cancelled`, `Expired`, `Rejected`
**Notes**
- Approved の場合:
  - `DecisionInfo(decidedByUserId, decidedAtUtc)` を保持
  - 状態型で “承認済みだが未確定” を表すのが重要（null排除の効果）
- Rejected の場合:
  - `RejectInfo(rejectedBy, rejectedAt, reason)` を保持
  - 必要なら hold を保持したままにする/解放済みにする方針を選ぶ  
    - 推奨: 却下と同時に Cancel/Expire 相当の解放を別の Committed イベントでやらず、  
      却下後は Confirm 不可になり、サガが Cancellation を発行する  
      - ただし “却下=終了” にしたいなら、却下イベントに解放情報を含める設計も可（今回は tags_and_events.md では Cancel/Expire に寄せる）

---

#### Event: ReservationConfirmedCommitted
**From**
- `Held`（承認不要ケース）
- `ApprovedAwaitingConfirm`（承認済みケース）
**To**
- `Confirmed`
**Invalid**
- `Draft`, `PendingApproval`, `Rejected`, `Cancelled`, `Expired`, `Confirmed`
**Notes**
- Allocation（poolId, allocatedEquipment, sourceHoldId）を必ず持つ（null排除）
- releaseHold=true の場合、Hold から Allocation に移行したとみなす

---

#### Event: ReservationCancelledCommitted
**From**
- `Draft`
- `Held`
- `PendingApproval`
- `ApprovedAwaitingConfirm`
- `Confirmed`
**To**
- `Cancelled`
**Invalid**
- `Cancelled`, `Expired`（二重キャンセル不可）
- `Rejected`（却下後に Cancel を許すかは方針次第。最小は不可でもOK）
**Notes**
- CancelInfo（cancelledBy/cancelledAt/reason）を保持
- “解放が同時に成立した” のは DCB イベントで担保される（Reservation側は事実として Cancelled を記録するだけ）

---

#### Event: ReservationExpiredCommitted
payload: `reason = HoldExpired | ApprovalExpired`

**From**
- `Held`（HoldExpired）
- `PendingApproval`（ApprovalExpired）
- `ApprovedAwaitingConfirm`（ApprovalExpired を許すかはポリシー次第）
**To**
- `Expired`
**Invalid**
- `Draft`, `Confirmed`, `Cancelled`, `Rejected`, `Expired`
**Notes**
- ExpireInfo(expiredAt, reason) を保持

---

#### Event: ReservationRescheduledCommitted (optional)
**From**
- `Draft`
- `Held`
- `PendingApproval`（避けるのが無難）
- `ApprovedAwaitingConfirm`（避けるのが無難）
**To**
- 同じ状態型のまま TimeRange を更新（ただし整合性が複雑化する）
**Notes**
- 推奨: Reschedule は “複合操作” として、新 reservation でやる or Hold→Approval→Confirm のイベント列で表す

---

## 2. ApprovalRequestState 遷移表

### 2.1 ApprovalRequestState（代表）
- `Pending`
- `Approved`
- `Rejected`
- `Expired`
- `Cancelled`

---

### 2.2 Events → ApprovalRequestState

#### Event: ApprovalFlowStarted
**From**
- `null / NotExists`
**To**
- `Pending`
**Invalid**
- 既に存在する場合（重複開始）
**Notes**
- requiredRole, deadline, reasons を保持

---

#### Event: ApprovalDecisionRecorded
payload: `decision = Approved | Rejected`

**From**
- `Pending`
**To**
- `Approved`（Approved）
- `Rejected`（Rejected）
**Invalid**
- `Approved`, `Rejected`, `Expired`, `Cancelled`
**Notes**
- decidedBy/decidedAt を保持
- Rejected は rejectReason を保持

---

#### Event: ReservationCancelledCommitted
**From**
- `Pending`（承認中に予約がキャンセル）
**To**
- `Cancelled`
**Invalid**
- `Approved` / `Rejected` / `Expired`（通常は不可）
**Notes**
- payload の `approvalCancelled=true` を必須にする運用だと安全
- CancelledInfo を保持するなら state に含める

---

#### Event: ReservationExpiredCommitted
**From**
- `Pending`
**To**
- `Expired`
**Invalid**
- `Approved` / `Rejected` / `Cancelled`
**Notes**
- payload の `reason = ApprovalExpired` かつ approvalRequestId が一致すること

---

## 3. EquipmentPoolState 遷移表（主要な更新）

EquipmentPool は DU ではなく “辞書 + レコード” 管理を推奨しているため、
ここでは「主要なレコードの増減」を遷移として定義します。

### 3.1 用語
- HoldRecord: `holdId -> (reservationId, timeRange, items, holdKind, expiresAtUtc)`
- AllocationRecord: `reservationId -> (timeRange, items, sourceHoldId)`

---

### 3.2 Events → EquipmentPoolState

#### Event: ReservationHoldCommitted（Succeeded のときのみ）
**Preconditions**
- tags に `EquipmentPool(poolId)` が含まれる
- holdId が未使用
**Apply**
- HoldsByHoldId[holdId] = HoldRecord(...)
**Invalid**
- holdId 重複
- 在庫不足/時間帯衝突は “コマンド側” で弾く前提（Apply では整合性確認を最小化）

---

#### Event: ReservationConfirmedCommitted
**Preconditions**
- tags に `EquipmentPool(poolId)` が含まれる
**Apply**
- AllocationsByReservationId[reservationId] = AllocationRecord(...)
- payload.releaseHold=true なら、sourceHoldId の HoldRecord を削除
**Invalid**
- allocation が既に存在（重複確定）
- sourceHoldId が存在しないのに releaseHold=true（方針によりエラー or 許容）

---

#### Event: ReservationCancelledCommitted
**Preconditions**
- tags に `EquipmentPool(poolId)` が含まれる場合のみ Apply
**Apply**
- payload.holdId があれば HoldsByHoldId から削除（存在すれば）
- payload.allocationReleased=true なら AllocationsByReservationId から削除（存在すれば）
**Invalid**
- 最小サンプルでは “存在しない削除” は許容してもよい（冪等性を優先）

---

#### Event: ReservationExpiredCommitted
**Preconditions**
- tags に `EquipmentPool(poolId)` が含まれる場合のみ Apply
**Apply**
- payload.holdId があれば HoldsByHoldId から削除
**Invalid**
- 最小サンプルでは冪等削除を許容してよい

---

#### Event: EquipmentPoolCreated / EquipmentStockAdjusted
**Apply**
- stock を初期化/更新

---

## 4. 不正遷移（InvalidTransition）をどう扱うか

サンプルとしては、次の方針が分かりやすいです。

- Apply で不正遷移を検出したら例外（または Reject）にする  
  → “イベントストリームを壊さない” を優先
- コマンド側は状態型で事前ガードするため、通常は Apply に到達しない
- ただしリプレイ/移行/バグ混入時の最後の砦として Apply を厳密にする

---

## 5. 状態型で許可/不許可を決める（実装の指針）

- コマンドは “どの状態型で受理できるか” を明示する
  - `CommitReservationHold`: Draft のみ
  - `StartApprovalFlow`: Held のみ
  - `RecordApprovalDecision`: ApprovalRequest.Pending + Reservation.PendingApproval のみ
  - `CommitReservationConfirmation`: Held or ApprovedAwaitingConfirm のみ
  - `CommitReservationCancellation`: Draft/Held/Pending/ApprovedAwaitingConfirm/Confirmed のみ
  - `CommitReservationExpiration`: Held/PendingApproval/(ApprovedAwaitingConfirm) のみ

この方針により、null 判定不要で、状態遷移の安全性が高まります。

もしこの次に「Apply をそのままコードに落とすテンプレ（C# record + switch）」「各イベントの tags と Apply 対象の対応（どのタグが付いてたらどの集約に Apply されるか）」も付けると、Sekiban DCB のサンプルとしてかなり完成度が上がります。