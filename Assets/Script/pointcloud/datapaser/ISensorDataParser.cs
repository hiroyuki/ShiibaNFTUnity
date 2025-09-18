using System.IO;

public interface ISensorDataParser
{
    /// <summary>
    /// フォーマット識別子（例: "RCST", "RCSV"）
    /// </summary>
    string FormatIdentifier { get; }

    /// <summary>
    /// 現在のレコードのタイムスタンプ（ParseNextRecordで更新）
    /// </summary>
    ulong CurrentTimestamp { get; }

    /// <summary>
    /// ヘッダー（YAML）を読み込み、内部構造を初期化
    /// </summary>
    void ParseHeader();

    /// <summary>
    /// 次のレコードを読み込み、状態（タイムスタンプや画像など）を更新
    /// </summary>
    bool ParseNextRecord();

    bool PeekNextTimestamp(out ulong timestamp);
}
