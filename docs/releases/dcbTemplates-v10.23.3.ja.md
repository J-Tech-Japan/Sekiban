# DCB テンプレート 10.23.3

Sekiban DCB 10.23.3 用の 5 つのスターターテンプレートです。生成されるプロジェクトは 10.23.3 のパッケージを参照します。テンプレートのコードは 10.23.2 から変わっていません。

## 含まれるライブラリの修正

5 つのテンプレートはすべて Orleans の materialized view を登録している（`AddSekibanDcbMaterializedViewOrleans()`）ので、生成されるプロジェクトには 10.23.3 のライブラリの修正が入ります。materialized view の grain が、catch-up の途中まで進んだときも、stream からの通知を保持するようになりました（issue #1339）。詳しくは 10.23.3 のライブラリのリリースノートをご覧ください。

## 生成済みのプロジェクト

テンプレートのパッケージを更新しても、生成済みのプロジェクトは変わりません。修正を取り込むには、`SekibanDcbTemplateVersion.props` の `SekibanDcbVersion` を `10.23.3` に変更してください。
