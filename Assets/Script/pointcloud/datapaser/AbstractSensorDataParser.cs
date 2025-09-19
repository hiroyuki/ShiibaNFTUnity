using System.IO;

public abstract class AbstractSensorDataParser : ISensorDataParser, System.IDisposable
{
    protected readonly BinaryReader reader;
    protected readonly string deviceName;

    public ulong CurrentTimestamp { get; protected set; }
    public SensorHeader sensorHeader { get; protected set; }

    public AbstractSensorDataParser(BinaryReader reader, string deviceName = "Unknown Device")
    {
        this.reader = reader;
        this.deviceName = deviceName;
    }

    public abstract string FormatIdentifier { get; }

    public abstract void ParseHeader();
    public abstract bool ParseNextRecord();
    public abstract bool ParseNextRecord(bool optimizeForGPU);
    public abstract void Dispose();

    public abstract bool PeekNextTimestamp(out ulong timestamp);

    /// <summary>
    /// 指定されたタイムスタンプのレコードを検索してパースする（デフォルト実装）
    /// 具象クラスで最適化された実装にオーバーライド可能
    /// </summary>
    public virtual bool ParseRecord(ulong targetTimestamp, bool optimizeForGPU)
    {
        try
        {
            // 無限ループ防止のため最大試行回数を設定
            const int maxIterations = 10000;
            int iterations = 0;
            
            while (iterations < maxIterations)
            {
                // 現在位置の次のタイムスタンプを確認
                if (!PeekNextTimestamp(out ulong currentTimestamp))
                {
                    // EOF到達 - 目標タイムスタンプが見つからない
                    return false;
                }
                
                // 目標タイムスタンプと比較
                if (currentTimestamp == targetTimestamp)
                {
                    // 目標に到達 - パースを実行
                    return ParseNextRecord(optimizeForGPU);
                }
                else if (currentTimestamp > targetTimestamp)
                {
                    // 目標を過ぎてしまった - 前方シークのみなので失敗
                    UnityEngine.Debug.LogWarning($"Target timestamp {targetTimestamp} not found. Current: {currentTimestamp} (forward seek only)");
                    return false;
                }
                else
                {
                    // まだ目標に到達していない - スキップして続行
                    if (!SkipCurrentRecord())
                    {
                        // スキップ失敗 - データ破損またはEOF
                        return false;
                    }
                }
                
                iterations++;
            }
            
            // 最大試行回数に到達
            UnityEngine.Debug.LogError($"ParseRecord exceeded maximum iterations ({maxIterations}) searching for timestamp {targetTimestamp}");
            return false;
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"Error in ParseRecord for timestamp {targetTimestamp}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 現在のレコードをスキップして次のレコードに移動
    /// 具象クラスで実装が必要
    /// </summary>
    public abstract bool SkipCurrentRecord();

    // 将来的に共通ヘルパー関数などをここに追加できます
}
