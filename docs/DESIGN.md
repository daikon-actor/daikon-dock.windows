# DaikonDock 設計メモ

利用者向けの説明は README.md（英語）。ここには「なぜこう作ったか」と内部の仕組みをまとめる。

## 1. 目的と前提

- トリガーキー（既定は Ctrl）の長押しで、タスクバーのすぐ上に同じ大きさのランチャーバーを出す。
- 表示中は常に最前面。アイコンのクリックで起動、もう一度の長押しか右端の「∨」ボタンで隠す。
- 項目は利用者が `data/*.json` に手で書く。
- **できるだけ軽く**。待機中の CPU はほぼ 0、追加のランタイムやインストールは不要にする。

### 技術的な制約（変えないこと）

| 制約 | 理由 |
|---|---|
| .NET Framework 4.8 | Windows 10 / 11 に標準で入っている。入っていない機能が必要なら仕様の方を削る |
| C# 5 | 利用者はソースを受け取り、Windows 付属の csc.exe（C# 5 まで）でビルドする |
| 外部パッケージなし | 単一 exe で配れる。csc.exe だけでビルドできる |
| WinForms + GDI+ の自前描画 | 子コントロールを使わず、軽くしてちらつきも抑える |

## 2. フォルダ構成

```
├─ build.bat                利用者向けの正式ビルド（csc.exe）→ dist\
├─ README.md                利用者向けの説明（英語）
├─ LICENSE.md               MIT License
├─ DaikonDock.sln           Rider 用
├─ Directory.Build.props    obj / bin を build\ に出す
├─ .editorconfig            コーディング規約・解析ルール
├─ data/apps.schema.json    設定ファイルの JSON Schema（dist\data に毎回コピー）
├─ data/en, data/ja         言語別のサンプル apps.json（dist\data に無いときだけコピー）
├─ lang/en.json, ja.json    画面の文言（exe に埋め込み + dist\lang に無いときだけコピー）
├─ docs/DESIGN.md           このファイル
└─ src/                     ソース（下記）
```

## 3. ソースの構成

「1ファイル1役割」。厳密な MVC にはせず、画面・データ・つなぎ役・OS とのやりとりに分けている。

| ファイル | 役割 |
|---|---|
| AssemblyInfo.cs | バージョンと製品情報 |
| Program.cs | 起動、二重起動の防止（Mutex）、DPI 対応の宣言 |
| AppController.cs | つなぎ役。トレイ・メニュー・設定の読み込み・言語・起動処理・警告の表示 |
| BarForm.cs | バーのウィンドウ。配置計算・アニメーション・マウス・ツールチップ |
| BarForm.Paint.cs | バーの描画だけ（partial class） |
| Config.cs | AppEntry / BarSettings、data フォルダの JSON の読み込みと変更検知 |
| Strings.cs | 画面の文言（lang ファイル） |
| HoldKeyDetector.cs | TriggerKey（キーの定義）と長押し検出 |
| IconLoader.cs | アイコンの取得と縮小 |
| Launcher.cs | 起動処理と、path の解決（PATH・App Paths） |
| NativeMethods.cs | Win32 API の宣言 |

依存の向きは、`AppController` から各部品への一方向。`BarForm` は起動処理を知らず、クリックを `ItemClicked` イベントで通知するだけ。

## 4. 処理の流れ

### 起動

1. `Program` が Mutex で二重起動を防ぎ、`SetProcessDPIAware` を呼ぶ。
2. `AppController` を作る。
   1. `Strings.Load(null)` で既定の言語を読む。
   2. data フォルダに設定ファイルが1つも無ければ、最小のサンプルを作る。
   3. `BarForm`（ウィンドウだけ作り、表示しない）、`HoldKeyDetector`、メニュー、トレイを作る。
   4. `Reload(true)` で設定を読む（次の「設定の読み込み」）。

### 設定の読み込み `Reload`

1. data 直下の `*.json`（`*.schema.json` を除く）をファイル名順に読み、内容を合成する。settings は後のファイルが優先され、apps は連結する。
2. `settings.language` で文言を読み直し、`settings.triggerKey` を解釈してメニューやトレイの文言を入れ直す。
3. `BarForm.SetConfig` で配置を計算し、表示される項目のアイコンだけを読み込む。
4. エラーがあればまとめて1つのダイアログに出し、入りきらない項目があれば警告を出す。

### 表示の切り替え `Toggle`

- 表示するときは、先に設定ファイルの変更を確認する（ファイル名と更新日時から作った文字列を比べるだけ）。変わっていれば `Reload` してから `ShowBar`。
- `ShowBar` のたびにタスクバーの位置を取り直すので、タスクバーを動かしても追従する。

## 5. 長押しの検出（HoldKeyDetector）

- **低レベルキーボードフック**（`WH_KEYBOARD_LL`）を使う。イベント駆動なので、ポーリングと違って待機中の負荷はほぼ 0。
- **マウスフックはキーを押している間だけ**設定する。キーを押したままのクリックやホイール（Ctrl+クリックでの複数選択、Ctrl+ホイールでの拡大）で発動させないため。
- キーを押すとタイマー（`holdMs`）を開始し、次の場合は取り消す。
  - キーを離した
  - 別のキーを押した（Ctrl+C など）
  - クリックやホイールを使った
- **キーリピートの判定**：フックが呼ばれた時点では非同期キー状態がまだ更新されていない。そのため `GetAsyncKeyState` が「押されていない」と返せば新しい押下と判断できる。ロック画面などでキーを離したことを取りこぼしても、状態が戻る。
- **Alt / Win のマスク**：Alt や Win は単独で離すと、メニューバーやスタートメニューが開く。発動した時点で未割り当てのキー（`0xE8`）を送り、「別のキーと一緒に押した」扱いにして打ち消す。このキーは自分のフックでは無視する。
- 選べるのは修飾キーだけ。文字キーなどは長押しすると文字が連続入力されるので対象外にした。
- 制約：管理者として動いているアプリが前面にあると、UIPI（権限の低いアプリから高いアプリへの操作を制限する Windows の仕組み）のためフックにキーが届かない。

## 6. バーの表示（BarForm）

- **ウィンドウの種類**：`WS_EX_NOACTIVATE`（フォーカスを奪わない）、`WS_EX_TOOLWINDOW`（Alt+Tab に出ない）、`WS_EX_TOPMOST`（最前面）。
- **位置**：`SHAppBarMessage(ABM_GETTASKBARPOS)` で、メインモニターのタスクバーの位置と向きを取る。取れないときは画面下端に置く。上・左・右向きのタスクバーにも対応している（Windows 11 は下だけ）。
- **アニメーション**：ウィンドウの見える幅を 0 からタスクバーの厚みまで変え、描画をずらしてタスクバーの裏からせり出すように見せる。進み方は ease-out（終わりに向かってゆっくり）。タイマーが動くのはアニメーション中だけ。
- **配置**：
  - アイコン1個の枠は、タスクバーの厚みと同じ大きさの正方形。区切り線は厚みの 1/4、隠すボタンは 2/3。
  - `align` は left / center / right。はみ出すと左詰めになる。
  - 入りきらない項目は表示もクリックもされず、`HiddenCount` で数える。
- **入りきらないとき**：設定を読み込むたびと、画面設定が変わって隠れる数が増えたときに警告ダイアログを出す。「»」ボタンで残りを出す案は、表示されているか利用者が分かりにくいので見送った。
- **描画**：子コントロールは使わず、すべて `OnPaint` で描く。背景は `OnPaint` 側で塗り、`OnPaintBackground` は何もしない（ちらつき防止）。

## 7. アイコン（IconLoader）

| 指定 | 取得方法 |
|---|---|
| `icon` が .png / .jpg / .bmp / .gif | 画像をそのまま読む（ファイルはロックしない） |
| `icon` が .ico、または `xxx.dll,3` | `PrivateExtractIcons` で表示サイズのアイコンを取る |
| 上記以外（exe・lnk・フォルダ・ファイル） | シェルのイメージリスト（48px か 256px）。エクスプローラーと同じアイコン |
| http / https の URL | `.html` に関連付けられたアイコン（既定のブラウザ） |
| 取れないとき（`ms-settings:` など） | 名前の頭文字を丸の中に描く |

- 表示サイズ（`iconSize` × DPI 倍率）に縮小したものを1回だけ作り、使い回す。DPI が変わったときだけ作り直す。
- 読み込むのは表示される項目だけ。取得に失敗した項目も「試した」と記録し、毎回は読み直さない。

## 8. 設定（Config）

- JSON の読み込みには、.NET Framework 標準の `JavaScriptSerializer`（System.Web.Extensions）を使う。外部パッケージを使わず、単一 exe にするため。
- キー名の大文字小文字は区別しない。項目が無いときや値の型が違うときは、既定値を使う。
- path の解決（`PathResolver`）：
  - URI（`https:`、`ms-settings:`、`shell:` など）はそのまま使う。
  - 絶対パスはそのまま、`\` を含む相対パスは exe のあるフォルダを基準に解決する。
  - 名前だけのとき（`notepad.exe` など）は、exe のフォルダ → System32 → Windows → PATH → App Paths レジストリの順に探す（アイコン取得用）。見つからなければ、そのまま ShellExecute に渡す。
- `apps.schema.json` で、エディタでの補完とチェックを効かせる。説明文は日本語。

## 9. 多言語対応（Strings）

- キーごとに、次の順で探す。
  1. exe 隣の `lang\{言語}.json`
  2. 埋め込みの `{言語}`
  3. exe 隣の `lang\en.json`
  4. 埋め込みの `en`
  5. キー名そのもの
- 既定の言語は build.bat で選ぶ（`/define:DAIKONDOCK_LANG_JA`）。設定の `language` で上書きできる。
- `Strings.Format` は言語ファイルの `{}` の書き間違いでは落ちない（そのまま表示する）。
- 文言を足すときは `lang\` のすべてのファイルにキーを追加する。
- 対象外：Windows が返すエラーの詳細は、OS の表示言語のまま。README は英語のみ。コードのコメントとスキーマは日本語。

## 10. ビルドと配布

### build.bat（正式）

1. 言語を英語で問い合わせる（`/lang:en` `/lang:ja` で省略できる。`/nopause` で最後の pause を省く）。
2. `csc.exe /recurse:src\*.cs` でまとめてコンパイルする。`.cs` を増やしても bat の修正は不要。
3. 埋め込むもの：`app.ico`（exe のアイコンとトレイ用のリソース）、`lang\*.json`。
   `app.ico` は `src/Resources/app.png`（256×256）から 16〜256px の 8 サイズを作ってまとめたもの。アイコンを変えるときは png を差し替えて ico を作り直す。
4. `dist\` にコピーするもの：

| コピー元 | コピー先 | 方針 |
|---|---|---|
| `data\{言語}\*.json` | `dist\data\` | 無いときだけ（利用者の設定を守る） |
| `lang\*.json` | `dist\lang\` | 無いときだけ（利用者の修正を守る） |
| `data\apps.schema.json` | `dist\data\` | 毎回上書き |
| `README.md`、`LICENSE.md` | `dist\` | 毎回上書き |

### Rider（開発用）

- `LangVersion=5` で、C# 6 以降の構文をエラーにする。
- 静的チェックは `AnalysisLevel=latest-recommended` と `EnforceCodeStyleInBuild`。Release では警告をすべてエラーにする。
- `Directory.Build.props` で obj / bin を `build\` に出す。`src` 内に生成された .cs を、build.bat の `/recurse` が拾わないようにするため。
- 既定の言語は csproj の `DaikonDockLang`（en / ja）。

### バージョン

- `src/AssemblyInfo.cs` だけで管理する。csc.exe はこの属性から exe のバージョン情報（プロパティの「詳細」タブ）を作る。
- csproj は `GenerateAssemblyInfo=false` にして、Rider でも同じファイルを使う（自動生成すると属性が重複するため）。
- トレイメニューの先頭に「DaikonDock 1.0.0」のように表示する（`AssemblyInformationalVersion`）。
- リリースのたびに 3 つの属性（AssemblyVersion / AssemblyFileVersion / AssemblyInformationalVersion）を更新する。

## 11. 採用しなかった案

| 案 | 見送った理由 |
|---|---|
| .NET 8 | 配布先でランタイムが必要になる。同梱すると数十MBになる |
| System.Text.Json などのパッケージ | dist に DLL が増え、csc.exe 単体でビルドできなくなる |
| CsWin32 による宣言の自動生成 | ソースジェネレーターが必要で、C# 5 / csc.exe では使えない |
| 入りきらない分を「»」ボタンで表示 | 表示されているか分かりにくい。警告だけにした |
| Windows の表示言語に自動で合わせる | 不要とした。ビルド時の選択と設定の `language` だけにする |
| 厳密な MVC でのファイル分割 | この規模ではファイルが細かくなりすぎる |

## 12. 既知の制限

- 管理者として動いているアプリが前面にあると、長押しを検知できない（UIPI）。
- 表示するのはメインモニターのタスクバーに合わせたバーだけ。
- タスクバーを自動的に隠す設定にしていると、隠れたタスクバーの分だけ隙間が空く可能性がある（未確認）。
- `dist\lang\` を書き換えても自動では反映されない。メニューの「設定を再読み込み」か再起動が必要。
- リポジトリ内の `data\en\apps.json` / `data\ja\apps.json` の `"$schema"` は、dist でのパス（`./apps.schema.json`）を指している。そのため、リポジトリ内で編集するときはスキーマによる補完が効かない。
