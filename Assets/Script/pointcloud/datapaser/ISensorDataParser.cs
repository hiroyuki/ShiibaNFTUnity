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

    /// <summary>
    /// 指定されたタイムスタンプのレコードを検索してパースする
    /// 現在位置から前方にのみシーク可能（後方シークは不可）
    /// </summary>
    /// <param name="targetTimestamp">目標タイムスタンプ</param>
    /// <param name="optimizeForGPU">GPU最適化フラグ</param>
    /// <returns>目標タイムスタンプのレコードが見つかってパースに成功した場合true、失敗した場合false</returns>
    bool ParseRecord(ulong targetTimestamp, bool optimizeForGPU);

    bool PeekNextTimestamp(out ulong timestamp);
}
