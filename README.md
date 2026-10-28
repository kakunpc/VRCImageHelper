# VRCImageHelper

VRChatのカメラで撮った画像を圧縮して、

* ワールド名
* インスタンスにいたプレイヤー名
* [VirtualLens2](https://logilabo.booth.pm/items/2280136)の
    * 絞り値
    * 焦点距離
    * 露出補正
* [Integral](https://suzufactory.booth.pm/items/4724145)の
    * 絞り値
    * 焦点距離
    * 露出補正
    * (多重露光時) 露光時間
    * レンズのボケの形状
* (MakerNotesに、Base64でエンコードされたJSONとして) World Name / World ID / Instance Owner / Permission / Playerのリスト

を書き込むツールです。

### インストール

1. インストーラを[Releases](https://github.com/m-hayabusa/VRCImageHelper/releases/)からダウンロードし、インストールする  
(インストール中に "You must install or update .NET to run this application." から始まるメッセージが出てきた場合、「はい」をクリックすると必要なライブラリ (.NET 10.0 Desktop Runtime (x64)) のダウンロードページが開くので、それをインストールしてからもう一度VRCImageHelperのインストール操作をしてください)

2. インストール中に設定画面が出てくるので、設定する  
    ![設定画面](https://github.com/m-hayabusa/VRCImageHelper/assets/10593623/8b2b56e3-f31e-4017-9c99-e2ce636e8bfd)

    * 保存先: 変換後のファイルを保存する場所。未設定の場合はVRChatが出力したフォルダに保存される

    * 保存形式
        * ファイル名: 保存するときのファイル名。デフォルトの場合はVRChatの出力したファイルと同等となるはず
            デフォルトの値は `yyyy-MM\VRChat_yyyy-MM-dd_hh-mm-ss.fff_XXXXxYYYY%{_LAYER}%.jpeg`

            | フォーマット         | 置換内容                   | 例                                          |
            | ------------------- | -------------------------- | ------------------------------------------- |
            | `yyyy`              | 撮影時刻: 年               | `2025`                                      |
            | `MM`                | 撮影時刻: 月               | `04`                                        |
            | `dd`                | 撮影時刻: 日               | `06`                                        |
            | `hh`                | 撮影時刻: 時               | `10`                                        |
            | `mm`                | 撮影時刻: 分               | `53`                                        |
            | `ss`                | 撮影時刻: 秒               | `56`                                        |
            | `fff`               | 撮影時刻: 秒(小数点以下)   | `149`                                       |
            | `XXXX`              | 画像のピクセル数 (横)      | `3840`                                      |
            | `YYYY`              | 画像のピクセル数 (縦)      | `2160`                                      |
            | `%{CAMERA}%`        | カメラの種類               | `VRCCamera`                                 |
            | `%{WORLD}%`         | ワールド名                 | `nS⁄TownScaper`                             |
            | `%{WORLD:ID}%`      | ワールド ID                | `wrld_3208d019-7310-4c35-b12e-e4278c2689c7` |
            | `%{INSTANCE:ID}%`   | インスタンス番号           | `99424`                                     |
            | `%{INSTANCE:TYPE}%` | インスタンスの種類         | `Friends+`                                  |
            | `%{OWNER:ID}%`      | インスタンスオーナーの ID  | `usr_cbced732-f21a-46cd-a6a6-61990bceea14`  |
            | `%{TAKEN:yyyy}%`    | 撮影時刻: 年               | `2025`                                      |
            | `%{TAKEN:MM}%`      | 撮影時刻: 月               | `04`                                        |
            | `%{TAKEN:dd}%`      | 撮影時刻: 日               | `06`                                        |
            | `%{TAKEN:hh}%`      | 撮影時刻: 時               | `10`                                        |
            | `%{TAKEN:mm}%`      | 撮影時刻: 分               | `53`                                        |
            | `%{TAKEN:ss}%`      | 撮影時刻: 秒               | `56`                                        |
            | `%{TAKEN:fff}%`     | 撮影時刻: 秒(小数点以下)   | `149`                                       |
            | `%{JOIN:yyyy}%`     | インスタンス Join 日時: 年 | `2025`                                      |
            | `%{JOIN:MM}%`       | インスタンス Join 日時: 月 | `04`                                        |
            | `%{JOIN:dd}%`       | インスタンス Join 日時: 日 | `05`                                        |
            | `%{JOIN:hh}%`       | インスタンス Join 日時: 時 | `14`                                        |
            | `%{JOIN:mm}%`       | インスタンス Join 日時: 分 | `19`                                        |
            | `%{JOIN:ss}%`       | インスタンス Join 日時: 秒 | `53`                                        |

            例えば、`%{JOIN:yyyy}%-%{JOIN:MM}%\%{JOIN:yyyy}%-%{JOIN:MM}%-%{JOIN:dd}%\%{WORLD}%_%{INSTANCE:TYPE}%_%{INSTANCE:ID}%\VRChat_yyyy-MM-dd_hh-mm-ss.fff_XXXXxYYYY_%{CAMERA}%.heic` と指定したとき、
            保存先フォルダが `D:\Pictures\VRChat` なら、`D:\Pictures\VRChat\2025-04\2025-04-05\nS⁄TownScaper_Invite_64792\VRChat_2025-04-06_10-53-56.149_3840x2160_VRCCamera.heic` に保存される

        * 形式・品質・オプション: PNG / JPEG / WEBP / AVIFが選択できる
            * PNG: 品質設定とオプションは、無視される
            * JPEG: エンコーダを `default` で利用する場合、品質設定は、0が最高、100が最低となって、オプションは無視される。エンコーダを `mjpeg` を選択した場合は、次の項目に準ずる
            * WEBP / AVIF: 品質設定は、ffmpegでそれぞれのエンコーダの
                | エンコーダ | 引数 | 最高品質 | 最高圧縮 |
                |:-|:-|:-|:-|
                | `libwebp` | `-quality` | `0` | `100` |
                | `libaom-av1` | `-crf` | `0` | `63` |
                | `libsvtav1` | `-crf` | `0` | `63` |
                | `av1_qsv` | `-q` | `?` | `?` |
                | `av1_nvenc` | `-cq` | `1` | `51` |
                | `av1_amf` | `-qp_i` | `0` | `255` |
                | `mjpeg` | `-q` | `1` | `31` |

                CPUで `libwebp`、`libaom-av1` と `libsvtav1` を利用する場合と、GeForce RTX 4070 Ti Super で `av1_nvenc` を利用した場合について動作を確認  
                Intel Arc A770で `av1_qsv`、AMD Radeon 780Mで `av1_amf` を利用できることは過去に確認したが、現在動作するかは未確認  
                NvEncの場合、色情報がyuv420に間引かれる挙動になる (https://github.com/m-hayabusa/VRCImageHelper/issues/40)  
                オプションは、ffmpeg に追加で渡す引数を入力できる たとえば libwebp で `-lossless 1` など
    * 保存形式(透過)  
        画像にアルファチャネルが含まれる場合の形式指定で、それ以外は上記 保存形式 と同じ。ただし:
        * JPEG: 非対応
        * AVIF: 透過に対応しないAV1エンコーダもある (手元の環境ではlibaom-av1しか透明度を処理できないようだった)
3. (もしVRChat Exif Writerをインストールしたことがあり、削除していない場合) VRChat Exif Writerを削除することについて確認メッセージが出るので、特に理由がなければ、そのまま削除してください

4. スタートメニューからVRCImageHelperを起動し、タスクバーに出てくるアイコン <img style="height:1em" src="https://github.com/m-hayabusa/VRCImageHelper/raw/master/VRCImageHelper/icon.ico"> を右クリック、自動起動のチェックをつけると、次回からPCにログインした際に自動で起動するようになる

### 「VRChatのログ出力がオフになっていませんか？」から始まる通知が表示された場合

VRChatの設定から、「Logging」を有効にする必要がある可能性があるので、確認が必要
![image](https://github.com/m-hayabusa/VRCImageHelper/assets/10593623/b4a22571-bf88-4353-80e3-908323dd2470)

### アンインストール

Windowsの 設定/アプリ/インストールされているアプリ からアンインストールできる (同時に設定ファイルも削除される)

### ExifToolとffmpegについて
内部でexiftool.exeとffmpeg.exeを利用するが、環境変数 PATH に設定されたディレクトリ内に見つからなければ、自動でダウンロードするので、こだわりがなければ用意する必要はない。自動でダウンロードされるものは:
* ExifTool: https://sourceforge.net/projects/exiftool/files/latest/download
* ffmpeg: https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip


### 開発・ビルド (.NET 10)

対象は Windows Forms (`net10.0-windows10.0.19041.0`)。Windows SDK の対象バージョン、設定JSON、画像処理、OSC、通知のコードと既存パッケージのバージョンは維持している。
`global.json` は正式版 .NET 10.0 SDK を選択し、インストール済みの最新feature band / patchを利用する。SDKはビルド用で、実行先に必要なのはDesktop Runtime。
アプリのRIDは `win-x64`、`SelfContained` は `false` を明示し、MSIのx64前提条件と一致させている。

アプリ本体のビルド (Windows / macOS):

```sh
dotnet restore VRCImageHelper/VRCImageHelper.csproj -p:EnableWindowsTargeting=true
dotnet build VRCImageHelper/VRCImageHelper.csproj -c Release -p:EnableWindowsTargeting=true --no-restore
```

Windows x64向け、ランタイム非同梱の配布ファイル生成:

```sh
dotnet publish VRCImageHelper/VRCImageHelper.csproj -c Release -r win-x64 --self-contained false -p:EnableWindowsTargeting=true
```

出力先は `VRCImageHelper/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/`。
実行先には .NET 10 Desktop Runtime (x64) が必要。macOSでのビルド・publish成功はWindowsでの動作確認を意味しない。

MSIはWindows上の .NET 10対応Visual StudioとVisual Studio Installer Projects拡張でビルドする。
`Setup/Setup.vdproj` は従来のPublish Itemsとカスタムアクションを維持し、Debug/Releaseとも .NET 10 Desktop Runtime (x64) を前提条件に指定している。
ビルド環境のPrerequisitesに当該ランタイムのbootstrapperパッケージがあることを確認すること。MacではそのパッケージIDの存在とMSIのランタイム検出は検証できていない。
アプリとMSIの製品バージョンは既存の `0.9.9` のまま。新しいMSIとして配布する際は、既存版からのアップグレード用に製品バージョン・ProductCode・PackageCodeをWindowsで管理し、更新テストを行うこと。
`.vdproj` は `dotnet build` ではビルドできないため、上記CLIコマンドはアプリの `.csproj` を対象としている。

2026-10-28の移行検証 (macOS arm64 / SDK 10.0.403):

* restore、Debug / Releaseビルド、Windows x64向けframework-dependent publishが成功。
* 出力exeがWindows x64 (PE32+) で、runtimeconfigが `Microsoft.WindowsDesktop.App` / `Microsoft.NETCore.App` 10.0.0を要求することを確認。
* NuGetの直接・間接依存の脆弱性監査では、使用ソース (`https://api.nuget.org/v3/index.json`) に既知の脆弱性報告なし。将来の安全性を保証するものではない。
* `Rug.Osc 1.2.5` の `NU1701` (.NET Framework向け依存) と、既存コードのnullable / 型名警告が残る。警告は抑制していない。
* 元リポジトリに自動テストプロジェクト・CIはない。Windows GUIの実行とMSIの生成・インストール検証は未実施。

Windowsでのリリース前確認:

* 既存config.jsonの読み込み・保存、日本語/英語の設定画面、100%/150%/200% DPIでの配置。
* 常駐アイコン、通知表示とクリック、自動起動の登録・解除、終了。
* コピーしたサンプル画像でPNG/JPEG/WEBP/AVIF、透過画像、Exifとファイル名、既存設定での処理結果を比較。
* VRChatログ監視、OSCQuery discoveryとOSC受信、VirtualLens2 / Integralのメタデータ。
* .NET 6のみの環境と .NET 10 Desktop Runtime (x64) の環境で、setup.exeの前提条件、MSIのインストール・更新・アンインストール。

移行時に参照したMicrosoft公式資料:

* [.NET移行の概要](https://learn.microsoft.com/en-us/dotnet/core/porting/)
* 互換性変更一覧: [.NET 7](https://learn.microsoft.com/en-us/dotnet/core/compatibility/7.0)、[8](https://learn.microsoft.com/en-us/dotnet/core/compatibility/8.0)、[9](https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0)、[10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10)
* [macOSでのWindowsターゲットのビルド](https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1100)
* [global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)
* [Installer Projectsと.NET (Publish Items / Desktop Runtime前提条件)](https://learn.microsoft.com/en-us/visualstudio/deployment/installer-projects-net-core)

WinFormsのDPIスケーリング変更はWindowsで要確認。BinaryFormatterの実行コードやシリアライズ済みオブジェクトのリソースはなく、危険な互換モードの有効化は不要だった。
