ICoreは共用するために作っている

そのため、
WithResultでは
IMulti*

WithoutResultでも
IMulti*

で同じ機能を使えている。

かつ、実際には、AOTで動かさなくても

ICommand, ICommandHandler 系もModelに移すのが理想ではある。
それを考えると、

Sekiban.Dcb.Core.Model (AoT) に ICore系は移しつつ

Sekiban.Dec.WithResult.Model (AoT)
Sekiban.Dec.WithoutResult.Model (AoT)

を作るのは避けられないのではないか