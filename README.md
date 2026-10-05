# Codex Pro Controller

Nintendo Switch Pro ControllerでCodex / ChatGPTデスクトップアプリを操作する、Windows用のC# / .NETアプリです。

**0.1: Bluetooth接続の初代Switch Pro Controllerを対象とした初版。** OpenAI・Nintendoの公式製品ではありません。

## できること

- ZL＋6ボタンで、固定した6つのローカルチャットを直接開く。
- 音声入力、チャット切り替え、コマンドメニューなどをボタンから操作する。
- 左スティックでスクロールする。
- ボタン割り当て、チャットID、スティックの遊び、振動を設定・保存する。
- ローカルCodexのフックイベントを受け取り、ランプと短い振動で知らせる。
- 切断後に再接続を試みる。最小化すると通知領域に入る。

操作は毎回起動時に無効です。設定画面で有効にし、Codex / ChatGPTアプリを前面にすると受け付けます。接続・有効化直後にすでに押されているボタンは発火しません。

## 起動

1. WindowsのBluetooth設定でPro Controllerを接続します。
2. 配布フォルダを展開し、`CodexProController.exe`を起動します。DLLなども同じフォルダに置いてください。
3. 「6つのチャット」にチャットIDまたは `codex://threads/<id>` のリンクを入れて保存します。Codexの「Copy chat deep link」から取得できます。
4. 「コントローラー操作を有効にする」を選び、Codexを前面にします。

GitHub Actionsの `codex-pro-controller-win-x64` artifactは.NETランタイムを同梱します。framework-dependentビルドを使う場合は[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)が必要です。

設定は `%LOCALAPPDATA%\CodexProController\config.json` に保存されます。壊れた設定ファイルは自動で上書きしません。

## 初期割り当て

| Proコン | 操作 |
| --- | --- |
| ZL＋A / B / X / Y / L / R | 固定チャット1 / 2 / 3 / 4 / 5 / 6 |
| A | Enter（フォーカス位置に応じて送信・決定・承認） |
| B | Escape |
| X | 音声入力（Ctrl＋Shift＋D、アプリで利用可能な場合） |
| Y | コマンドメニュー（Ctrl＋Shift＋P） |
| L / R | 前 / 次のチャットまたはタブ |
| ＋ | 新しいチャット |
| − | サイドバー表示切り替え |
| HOME | 次の注意が必要なチャット |
| Capture | スキル画面 |
| 十字キー | 矢印キー |
| 左スティック上下 | マウスホイール（ポインター位置の画面） |

キーボードフォーカスやマウスポインターの位置は自動移動しません。EnterなどはCodex内でも現在フォーカスしている場所に届きます。

「ボタン割り当て」の操作種別:

| 種別 | 値 |
| --- | --- |
| `Shortcut` | Windows Forms SendKeys形式。`^`=Ctrl、`+`=Shift、`%`=Alt。例: `^+d`、`{F13}` |
| `OpenSkills` | スキル画面を開く。値は不要 |
| `SkillPrompt` | 新規チャットの入力欄に入れる文字列。例: `$my-skill 変更をレビューして`。自動送信しない |
| `None` | 操作しない |

推論の強さにはYからコマンドメニューを使うか、CodexのKeyboard Shortcutsで目的のコマンドにキーを割り当て、そのキーを `Shortcut` に登録してください。Micro専用の推論スライダーや長押しのpush-to-talkは初版では再現していません。

## チャット状態をランプに反映する

### フックの設定

1. アプリの「フック設定を生成」でJSONを保存します。生成時の実行ファイルの絶対パスが入るので、先にアプリの配置先を決めてください。
2. Codexのアクティブな設定レイヤーの `hooks.json` に内容を追加します。例: Windowsの `%USERPROFILE%\.codex\hooks.json`、またはプロジェクトの `.codex/hooks.json`。既存のフックがある場合は `hooks` の各イベント配列に追加してください。
3. ローカルCodexの `/hooks` で定義を確認し、信頼します。新しいフックや変更したフックは信頼されるまで実行されません。
4. 対象チャットを6つの枠に登録し、「通知対象」で選択します。

フックはローカルオーケストレーション向けです。クラウド実行・リモートホストのチャットには、このWindowsアプリだけでは連動しません。未取得の状態を完了として表示することはありません。

Windowsの `commandWindows` はPowerShell経由でJSONを標準入力へ渡します。WSL用の `command` はドライブのパスを `/mnt/c/...` 形式へ変換して生成します。標準以外のマウント先を使う場合はこのパスを変更してください。WSLのWindows実行ファイル相互運用が必要です。`--events` はWindowsパスのまま使います。

フック受信では、チャットID・状態・受信時刻だけを `%LOCALAPPDATA%\CodexProController\events` に保存します。プロンプト、会話内容、ツールの引数は保存しません。フックは `{}` を返し、承認や実行方針を変更しません。

### 表示の意味

ランプ1〜3は選択枠の番号を2進数で示します（左から1、2、4）。6枠を同時に個別表示する仕様ではありません。

| 最後に観測したイベント | 表示 |
| --- | --- |
| 未取得 | ランプ消灯 |
| SessionStart / Idle | 選択枠の番号を点灯 |
| UserPromptSubmit / PreToolUse / PostToolUse | 選択枠＋ランプ4点滅、HOMEがゆっくり点滅 |
| PermissionRequest | 4ランプ点滅、設定に応じて短い振動 |
| Stop | 選択枠＋ランプ4点灯、設定に応じて短い振動 |
| Interrupt / SessionEnd | 選択枠の番号のみ点灯 |

`Stop` は停止フックを観測したという意味で、作業の成功や受入完了を証明しません。他のフックが継続を要求する場合もあります。画面には最終観測時刻を表示します。初回読込や別枠への切り替えでは、保存済みのStopイベントを振動で再通知しません。

「通知テスト」はランプ・振動だけを動かし、実際のチャット状態を変更しません。

## 開発

[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)を使用します。

```sh
dotnet run --project tests/CodexProController.Tests -c Release
dotnet build src/CodexProController -c Release
dotnet publish src/CodexProController -c Release -r win-x64 --self-contained true -o publish
dotnet publish src/CodexProController.Hooks -c Release -r win-x64 --self-contained true -o publish
```

Coreの検証はWindows・Linux両方で実行できます。UIと実機の検証はWindowsで行います。

```powershell
# 入力取得だけを確認。結果をJSONに出力する。
.\CodexProController.exe --probe --out probe.json

# ランプ設定、HOMEパターン設定、短い振動も確認。終了前にランプを消灯する。
.\CodexProController.exe --probe --feedback --out probe.json
```

`feedbackAcknowledged` はランプ設定の応答と振動送信が成功したことを表します。見た目や体感の確認を代替しません。

## 現在の範囲

- 対象: Windows x64、Bluetoothの初代Switch Pro Controller（VID 057E / PID 2009）。USB、Switch 2 Pro、互換コントローラーは未検証。
- ボタン操作・ランプ・振動の実機通信、設定保存、入力の発火条件、フック受信は検証済み。実際の音声入力や人が押す全ボタン、Codexフックからの実チャット連動は別途確認が必要。
- スロットは手動設定。Microの「優先チャット自動割り当て」やキーごとのRGB表示は含みません。
- 状態フックは実行ホストとWindowsのアプリをつなぐ設定が必要です。OpenAIアプリの内部データベースや会話ログは読みません。

## 依存ライブラリ・参照

- [JoyCon.NET 1.0.1](https://github.com/ClusterM/joycon/tree/e83a1f2beb5a95d3c35d9a074e9be21981e1a8ea) — GPL-3.0、ボタン・校正・ランプ・振動。NuGetパッケージに記録されたソースコミットへのリンクです。
- [HidSharp](https://github.com/IntergatedCircuits/HidSharp) — Apache-2.0、HID通信
- [Codex / ChatGPTの公式ショートカットとディープリンク](https://learn.chatgpt.com/docs/reference/commands)
- [Codexの公式フック](https://learn.chatgpt.com/docs/hooks)

License: GPL-3.0-only（JoyCon.NET依存を含むこの版）。HidSharpおよび.NETランタイムのライセンス・通知も `third-party/` に同梱します。
