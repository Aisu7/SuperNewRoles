# 起動時パッチ処理の事前生成

通常のビルドで、DLLを配布先へコピーする前に次の加工を行います。ゲーム実行時にMono.Cecilを追加する必要はありません。

- CustomRPC: SNRのmanagedメソッド先頭へ送受信判定を挿入し、187件の起動時Harmonyパッチを省きます。
- Harmony registry: パッチクラスの一覧と、対応できるPrefix/Postfixの`HarmonyMethod`を構築するILを生成します。型・メソッドはハンドルから取得し、属性探索と属性情報のマージを省きます。
- 起動時の一括適用: 連続する対応可能なクラスをまとめ、同じ対象のラッパー再生成を減らします。`Prepare`等を持つクラスの前後はまたぎません。

**Harmonyの呼び出しラッパーやnative-to-managed trampoline自体は、まだ事前生成していません。** それらはインストール済みHarmony / Il2CppInteropが生成します。AndroidではStarlightの`SafeHook`経路、戻り値用バッファ処理、他Modとのパッチ合成を維持します。この段階だけで起動時間が半分になるとは限りません。

生成できない属性や動的な対象選択は、元のクラスプロセッサへ戻します。クラスの優先度はHarmony 2.10.2と2.16で合成規則が違うため、事前生成しません。未知のHarmonyバージョンや内部フィールド構成では一括適用も使用しません。

## ビルドと比較

通常ビルドはRPC weaving・一括適用・registry生成が有効です。ビルド環境には.NET 8 SDK以上と既存のAmong Us/BepInEx参照が必要です。

```powershell
# ゲームを自動起動しない通常ビルド
dotnet build SuperNewRoles/SuperNewRoles.csproj -p:SkipGameLaunch=true

# RPC高速化のみ（一括適用とregistryを無効）
dotnet build SuperNewRoles/SuperNewRoles.csproj -p:SkipGameLaunch=true -p:EnableHarmonyBatching=false

# 一括適用のみを追加し、registry生成は無効
dotnet build SuperNewRoles/SuperNewRoles.csproj -p:SkipGameLaunch=true -p:EnableHarmonyRegistry=false

# RPCも従来方式へ戻して比較
dotnet build SuperNewRoles/SuperNewRoles.csproj -p:SkipGameLaunch=true -p:EnableHarmonyBatching=false -p:EnableRpcWeaving=false
```

`[LoadTiming]`と`[HarmonyBatch]`で適用時間・件数・事前生成対象数を記録します。`batchApplyMs`は一括処理部分だけで、特殊クラスやコルーチンを含む総時間は`Harmony PatchAll`です。既存ログは維持しています。

## 検証

```powershell
dotnet test SuperNewRoles.RpcWeaver.Tests/SuperNewRoles.RpcWeaver.Tests.csproj

# PCの実際のHarmony/.NET 6で、順序、__state、元処理の抑止、他Modの前後追加を比較
dotnet test SuperNewRoles.Harmony.Tests/SuperNewRoles.Harmony.Tests.csproj -p:GenerateFixture=true

# 実際のSNR全体について、生成情報と通常Harmonyの解釈・発見順を比較
$env:SNR_CORPUS_ASSEMBLY = (Resolve-Path SuperNewRoles/bin/Debug/net6.0/SuperNewRoles.dll).Path
dotnet test SuperNewRoles.Harmony.Tests/SuperNewRoles.Harmony.Tests.csproj -p:GenerateFixture=true -p:EnableCorpusTests=true

# Android APKから抽出したcoreを使う場合（.NET 10 SDK/runtimeが必要）
dotnet test SuperNewRoles.Harmony.Tests/SuperNewRoles.Harmony.Tests.csproj -p:GenerateFixture=true -p:EnableCorpusTests=true -p:TestRuntime=net10.0 -p:HarmonyCore=C:/path/to/extracted/core

dotnet test SuperNewRoles.Tests/SuperNewRoles.Tests.csproj -p:SkipGameLaunch=true
```

PC同梱の旧MonoModは.NET 8でHarmonyの初期化自体が異常終了するため、Harmonyを動かすテストは通常の.NET 8テストプロジェクトから分離しています。APKのmanagedライブラリをWindows上で検証しても、Androidのネイティブフック実機検証の代わりにはなりません。
