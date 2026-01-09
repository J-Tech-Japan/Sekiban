# policies_sagas.md

このドキュメントは「会議室予約＋設備＋承認（ロール承認）」サンプルにおける
**ポリシー / サガ（プロセス）**の位置づけと、特に **Dynamic Consistency Boundary (DCB)** によって
「複数集約にまたがる一貫性」をフレームワークがどのように確保できるかを説明します。

---

## 1. Dynamic Consistency Boundary (DCB) とは（このサンプルでの意味）

一般的なイベントソーシングでは、強い一貫性の境界は「集約（Aggregate）」単位です。
複数集約にまたがる整合性（例: Reservation と EquipmentPool）を保つために、外側で
サガ（オーケストレーション）や、最終的整合性を受け入れる設計に寄りがちです。

このサンプルが採用する **Dynamic Consistency Boundary (DCB)** は、次の考え方です。

- **1つのイベントを「複数の集約に紐付けて」記録できる**
- その結果、フレームワークが
  - 同一イベントに紐付いた複数集約の更新を **同じ整合性境界の中** で扱える
  - 「部分成功（Reservationだけ更新され、EquipmentPoolは更新されない）」のような中間状態を
    **イベントレベルで発生させない（または回復可能にする）**
- つまり DCB では、整合性境界が固定の集約ではなく、
  **ユースケースごとに動的に組み立てられる**（＝Dynamic）。

このサンプルでは、典型的に次の一貫性要件を DCB で扱います。

- 予約確定時に
  - `Reservation` の状態が `Confirmed` になる
  - 同じイベントで `EquipmentPool` の割当が `Allocated` になる
  - （承認が必要なら）承認の事実が `ApprovalRequest` / `Reservation` に反映される
- これらが **同一イベント**（または同一イベント系列）として複数集約に紐付くことで、
  「予約は確定したが設備が確保できていない」などの矛盾を避ける。

---

## 2. このサンプルでの “ポリシー” と “サガ” の役割

DCB を採用しても、ポリシー/サガは不要にはなりません。
ただし役割が変わります。

### 2.1 ポリシー（Policy）
**「判断」**に責務を寄せます。

- 承認が必要か（OutsideBusinessHours / ExternalGuests / HighCostEquipment）
- hold の期限（Normal / Approval）
- requiredRole（FacilitiesApprover）
- 予約変更を許可するか

> ポリシーは「状態遷移そのもの」より、「判断材料と結果」を明示するために使うと
> サンプルとして分かりやすい。

### 2.2 サガ（Saga / Process）
**「進行」**に責務を寄せます。

- 予約作成 → 仮確保 → 承認 → 確定 の手順
- タイムアウト（Hold期限切れ / 承認期限切れ）
- UI/外部システム（通知等）を絡めた進行

ただし DCB により、サガの最重要な苦労である
「複数集約更新の部分成功をどう扱うか」が軽くなります。

- サガは “複数集約の整合性を自前で作る” のではなく
- **DCBで整合性が確保されるイベントを発行する**ために存在する
（または、イベントを発行するだけで多集約が同時に進む）。

---

## 3. “DCB を使う” とは具体的にどういうことか（イベントの紐付け）

このサンプルでは、次の「ビジネス的に不可分」な更新を **同一イベント**として扱います。

### 3.1 Hold 成功（設備仮確保＋予約状態更新）
- `EquipmentHoldPlaced`（EquipmentPool）
- `ReservationHoldSucceeded`（Reservation）

通常だと「設備側成功 → 予約側更新」という二段階になり、
途中失敗・再実行・補償が必要になります。

DCB では、これらを **同一イベントに同時紐付け**することで、
フレームワークが「同じ整合性境界内の更新」として扱えます。

**意図**:
- Hold の成功/失敗は、Reservation と EquipmentPool の双方に
  “同じ事実” として刻まれるべき。
- したがって Hold 成功は **単一の事実（1イベント）** として扱う。

### 3.2 承認（承認事実＋予約状態更新）
- `ApprovalGranted`（ApprovalRequest）
- `ReservationApprovalGranted`（Reservation）

承認は単独で意味を持ちますが、予約確定プロセスにおいては
Reservation 側も同じ事実を反映していることが重要です。

DCB では「承認した」という事実を、ApprovalRequest と Reservation の両方に
同じイベントで紐付けられます。

### 3.3 Confirm（確定＋設備割当確定）
- `EquipmentAllocated`（EquipmentPool）
- `ReservationConfirmed`（Reservation）

この2つは、ユーザー体験上も実務上も “同時であるべき” です。
DCB により、これを単一イベントとして扱い、部分成功を排除します。

---

## 4. ポリシー定義（判断ロジック）

### 4.1 ApprovalRequiredPolicy
**Input**
- Reservation: `timeRange`, `hasExternalGuests`, `requiredEquipment`
- Room: `openHours`, `alwaysRequiresApproval`
- EquipmentPool: （高価設備の定義は設定でも良い）

**Output**
- `approvalRequired: boolean`
- `reasons: string[]`
- `requiredRole: string`（例: `FacilitiesApprover`）
- `approvalDeadlineUtc: string`
- `holdKind: "Approval" | "Normal"`
- `holdExpiresAtUtc: string`

**Notes**
- 「承認が必要なら holdKind=Approval、期限を長め」など、
  “プロセスの方針” もポリシーが返すと一貫したサンプルになる。

---

## 5. プロセス（サガ）: Reservation Lifecycle

ここでは “UI操作” を起点に、どのイベントがどの集約へ紐付くかを明示します。
（※「コマンド」より「イベント系列」を中心に説明します）

### 5.1 Draft → Hold

1) ユーザーが Draft 作成/更新
- `ReservationDraftCreated`（Reservation）
- `ReservationDetailsUpdated`（Reservation）

2) ユーザーが「仮確保」実行
- `ReservationHoldRequested`（Reservation）

3) サガが必要情報を読み、ポリシーを評価
- Room を参照
- Reservation を参照
-（必要なら）EquipmentPool の在庫方針を参照

4) Hold を実行（DCB）
- 成功時: **1イベント**が同時に紐付く
  - `EquipmentHoldPlaced`（EquipmentPool）
  - `ReservationHoldSucceeded`（Reservation）
- 失敗時:
  - `EquipmentHoldRejected`（EquipmentPool）※任意（監査したければ）
  - `ReservationHoldFailed`（Reservation）

> ここが DCB の最初の見せ場。
> Hold 成功は “設備と予約の両方で成立してはじめて成功” なので、
> **単一イベントで複数集約に刻む**のが自然。

### 5.2 Held → PendingApproval / Confirmed

5) 承認要否の決定（DCB でも単独でもよい）
- `ReservationApprovalRequiredDetermined`（Reservation）

6-a) 承認が不要なら Confirm へ
- **1イベント（DCB）**
  - `EquipmentAllocated`（EquipmentPool）
  - `ReservationConfirmed`（Reservation）

6-b) 承認が必要なら ApprovalRequest 作成へ
- **1イベント（DCB）**
  - `ApprovalRequested`（ApprovalRequest）
  - `ReservationSubmittedForApproval`（Reservation）
  - （必要なら）`EquipmentHoldPlaced` を holdKind=Approval で更新/延長する場合もここで同時に

> 承認依頼の作成も、Reservation の状態遷移と不可分。
> “承認待ちになったのに承認依頼が存在しない” を避ける。

### 5.3 PendingApproval → Approved/Rejected → Confirmed / Cancelled

7) 承認者（FacilitiesApprover ロール）が承認
- **1イベント（DCB）**
  - `ApprovalGranted`（ApprovalRequest）
  - `ReservationApprovalGranted`（Reservation）

8) 承認後の Confirm
- **1イベント（DCB）**
  - `EquipmentAllocated`（EquipmentPool）
  - `ReservationConfirmed`（Reservation）

9) 却下
- **1イベント（DCB）**
  - `ApprovalRejected`（ApprovalRequest）
  - `ReservationApprovalRejected`（Reservation）
  - `EquipmentHoldReleased`（EquipmentPool）※承認待ち hold を解放するなら同時

10) 承認期限切れ
- **1イベント（DCB）**
  - `ApprovalExpired`（ApprovalRequest）
  - `ReservationApprovalExpired`（Reservation）
  - `EquipmentHoldReleased`（EquipmentPool）※同上

---

## 6. 一貫性確保の文章（サンプルとして入れたい説明）

このサンプルの一貫性要件は、次の3点に集約されます。

### 6.1 Reservation と EquipmentPool の整合性
- Reservation が Held/Confirmed のとき、対応する設備確保が存在する
- Reservation が Cancelled/Expired/Rejected のとき、設備確保が解放されている

DCB では、これらを **同一イベントを複数集約へ紐付ける**ことで保証します。
結果として、ユーザーが観測する状態（Read Model）でも
「予約は確定しているのに設備がない」といった矛盾が原理的に発生しにくくなります。

### 6.2 Reservation と ApprovalRequest の整合性
- Reservation が PendingApproval のとき、必ず対応する ApprovalRequest が Pending
- ApprovalRequest が Approved/Rejected/Expired のとき、Reservation にも同じ結果が反映される

これも同様に、承認結果を **単一イベントとして両集約へ紐付ける**ことで担保します。

### 6.3 “部分成功” の排除（イベントの原子性）
サガ/オーケストレーションで最も難しいのは「途中失敗で中間状態が残る」ことです。

DCB を用いると、ビジネス上不可分な更新を “単一イベント” にまとめられるため、
- 予約だけ状態が進む
- 設備だけ確保され続ける
- 承認結果だけ反映されない

といった **部分成功** を設計上の中心課題から外すことができます。

> サガは補償を前提とする“複雑な分散トランザクション”ではなく、
> DCB で確実に刻まれるイベントを発行し、
> タイムアウトや通知などの “進行管理” に集中できます。

---

## 7. サンプルとしての “見せ方” のポイント

- DCB を使う場面を、次の3つに絞ると理解されやすい
  1) Hold 成功（ReservationHoldSucceeded + EquipmentHoldPlaced）
  2) 承認結果（ApprovalGranted/Rejected + ReservationApprovalGranted/Rejected）
  3) Confirm（ReservationConfirmed + EquipmentAllocated）

- それ以外（単純なマスタ更新など）は通常の単一集約イベントのままで良い
  → DCB の価値が際立つ

---

## 8. 失敗・リトライ・冪等性（最小指針）

DCB により部分成功は減るが、サンプルとして次を明記すると実用感が出る。

- すべてのコマンドは `commandId` / `correlationId` を持つ前提にできる
- DCB イベントは “同じ目的” の重複発行を避ける（ReservationId + Phase などで冪等）
- タイムアウトはシステムユーザーで `Expire*` コマンドを起点にし、
  Expire イベントを DCB で複数集約へ紐付ける（期限切れと解放を同時に刻む）

---

## 9. 用語の対応（読者向け）

- Policy: ルール判定（承認が必要か、期限はどれくらいか）
- Saga/Process: 手順管理（Hold→Approval→Confirm、期限切れ処理）
- DCB: “不可分な事実” を **1イベントとして複数集約に紐付け**、一貫性境界を動的に作る仕組み