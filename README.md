# ActivityMonitor.Windows

macOS の Activity Monitor 風の軽量プロセスモニタ（MVP）。

- .NET 10 + C# + WinUI 3（unpackaged / self-contained）
- プロセス取得: `NtQuerySystemInformation` で一括取得、CPU% は差分から自前計算
- UI: 仮想化 ListView、行オブジェクトを使い回して差分更新（スクロール・選択が飛ばない）

## 機能（MVP）
プロセス名 / PID / CPU% / CPU時間 / スレッド数 / メモリ(Private WS)、検索、列ソート、
更新間隔切替（0.5 / 1 / 2秒）、プロセス終了、CPU LOAD バー（User / System / Idle）

## ビルド・実行
要件: Windows 10 1809+ / .NET 10 SDK（Visual Studio の場合は「WinUI アプリケーション開発」ワークロード）

    dotnet run --project ActivityMonitor.UI -p:Platform=x64

または `ActivityMonitor.slnx` を Visual Studio で開き、x64 で実行。

## 構成
    ActivityMonitor.Core
      Models/      ProcessInfo, SystemSnapshot, ProcessSnapshot
      Native/      NtDll (P/Invoke), NativeProcessReader
      Collectors/  ProcessCollector
    ActivityMonitor.UI   WinUI 3

Native / Collectors は将来 C++ の Native.dll に差し替えやすいよう Core 内でフォルダ分離している。
