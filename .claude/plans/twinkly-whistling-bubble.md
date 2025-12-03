# リファクタリング計画: BvhPlayableBehaviour から Joint Hierarchy 作成ロジックを抽出

## 目的
`BvhPlayableBehaviour` から `CreateJointHierarchy()` と `CreateJointRecursive()` メソッドを抽出して、複数のコンポーネント（Timeline 再生、ビジュアライザー、フレームアプライア、シーンフロー計算など）で再利用可能なスタンドアロンユーティリティクラスを作成する。

## 現状分析

### BvhPlayableBehaviour 内の既存メソッド
- **CreateJointHierarchy()** (Line 163-186): ルートジョイントを作成し、再帰的な子の作成を開始
- **CreateJointRecursive()** (Line 191-211): 子ジョイント GameObject を再帰的に作成
- **呼び出し元**: `OnGraphStart()` メソッド (Line 54) - Timeline ライフサイクルコールバック
- **主要特性**: Idempotent - 作成前に `Find()` で既存 transform をチェック

### 現在の依存関係
- **入力**: ジョイント階層構造を持つ `BvhData` オブジェクト
- **出力**: 正しく名付けられた GameObjects を持つ Transform 階層
- **使用元**: BvhPlayableBehaviour（主要）、BvhSkeletonVisualizer、BvhFrameApplier、SceneFlowCalculator（潜在的）

### 問題点
- 階層作成ロジックが Timeline ライフサイクルに密結合している
- コード重複なしで他のコンポーネント側で再利用できない
- 単独でテストするのが難しい
- 他のコンポーネントがジョイント階層を独立して作成することを妨げている

## 推奨アプローチ

### ステップ 1: 新しいユーティリティクラス `BvhJointHierarchyBuilder` を作成
**場所**: `Assets/Script/bvh/BvhJointHierarchyBuilder.cs`

**責務**:
- ジョイント階層作成のための静的ユーティリティクラス
- ジョイント GameObject 作成ロジックをカプセル化
- Idempotent な作成をサポート（既存 transform をチェック）
- フレーム非依存の階層構築をサポート（フレームに関わらず構造は同じ）

**パブリックメソッド**:
```csharp
public static Transform CreateOrGetJointHierarchy(BvhData bvhData, Transform parent)
  → BvhData 構造からスケルトン階層全体を作成、ルートジョイント transform を返す
  → 階層はフレーム非依存（すべてのフレームで同じ構造）
  → Timeline 再生、ビジュアライザー、SceneFlowCalculator で安全に使用可能

public static Transform CreateOrGetJoint(BvhJoint joint, Transform parent)
  → 単一ジョイント transform を作成または取得して返す
  → 再帰的階層作成で使用される内部ヘルパーメソッド
```

**実装の詳細**:
- 作成前に `Find()` で既存 transform をチェック
- BvhJoint データから適切な offset、rotation、scale で transform を初期化
- End site を適切に処理（作成をスキップ）
- 階層作成イベントのデバッグログを追加
- Timeline システムまたは特定フレームへの依存なし
- フレーム適用は `BvhFrameApplier` で別途実施

### ステップ 2: BvhPlayableBehaviour をリファクタリング
- `CreateJointHierarchy()` メソッドを新しいユーティリティクラスへの呼び出しに置き換え
- `CreateJointRecursive()` メソッドを新しいユーティリティクラスへの呼び出しに置き換え
- 互換性のため内部 `CreateJointHierarchy()` を薄いラッパーとして保持したい場合はそのまま
- プライベート再帰ヘルパーメソッドを削除

**OnGraphStart() での変更**:
```csharp
// 旧:
CreateJointHierarchy();

// 新:
BvhJointHierarchyBuilder.CreateOrGetJointHierarchy(bvhData, BvhCharacterTransform);
```

### ステップ 3: 関連するコンポーネントを更新
これらのコンポーネントが直接ユーティリティを使用できることを認識させる:

- **BvhSkeletonVisualizer**: 欠落している場合は階層作成をトリガー可能（既に Timeline を 1 秒待機）
- **BvhFrameApplier**: 既にフォールバック作成ロジックあり；推奨アプローチとしてドキュメント
- **SceneFlowCalculator**: **ユーティリティの主要な新規ユーザー**
  - 現在、内部で `CreateBoneHierarchy()` を呼び出し（重複ロジック）
  - `BvhJointHierarchyBuilder.CreateOrGetJointHierarchy()` でメインスケルトン作成を置き換え可能
  - フレーム視覚化中の一時スケルトン作成で使用可能 (`CreateTemporaryBvhSkeletonWithScale()`)
  - フレーム適用は `BvhFrameApplier` で別途実施（階層存在後にフレームデータを適用）
  - ユーティリティはフレーム非依存の階層を作成；フレームは `ApplyBvhFrame(int bvhFrame)` メソッドで適用
- **TimelineController**: 直接変更不要

### ステップ 4: 包括的なドキュメント追加
- `BvhJointHierarchyBuilder` に使用例付きの XML ドキュメント追加
- Idempotent な動作をドキュメント化（複数回呼び出しても安全）
- さまざまなシナリオの使用パターン例を含める

## 主要な設計決定

### フレーム非依存の階層作成
- **インサイト**: ジョイント階層構造はすべてのフレームで同じ - BVH ファイルは固定スケルトン構造を持つ
- **フレームデータ** は位置/回転のみを変更、階層自体は変更されない
- **含意**: 1 つの階層がすべてのフレームに対応；フレームデータは階層存在後に `BvhFrameApplier` で適用
- **SceneFlowCalculator への利益**: スケルトンを 1 度作成して、階層をリビルドせず異なるフレームを適用可能

### 静的ユーティリティクラスである理由
- **根拠**: ジョイント階層作成はステートレス操作 - インスタンス状態不要
- **利益**: シンプル API、オブジェクト作成のオーバーヘッドなし、発見しやすい
- **検討した代替案**: インスタンスベースのビルダー - 不要な複雑さのため却下

### Idempotent である理由
- **根拠**: 他のコンポーネントも階層作成を試みる可能性、または複数回呼び出しされる可能性
- **利益**: タイミング問題なしに統合できるセーフティ
- **実装**: 作成前に `Find()` で既存 transform をチェック

### 別ファイルである理由
- **根拠**: 単一責任の原則に従う；BvhPlayableBehaviour は Timeline 再生を管理、階層作成ではない
- **利益**: 関心の明確な分離、単独でテスト可能、他のコンポーネントから発見可能

## 修正対象ファイル

1. **新規作成**:
   - `Assets/Script/bvh/BvhJointHierarchyBuilder.cs` (新しいユーティリティクラス)

2. **既存ファイル修正**:
   - `Assets/Script/timeline/BvhPlayableBehaviour.cs` (メソッドをユーティリティ呼び出しに置き換え)

3. **ドキュメント更新**:
   - 新しいユーティリティクラスの XML ドキュメントに使用例を追加
   - `CLAUDE.md` を新しいユーティリティクラスへの参照で更新
   - 階層作成をドキュメント化している場合は `BVH_TIMELINE_USAGE.md` を更新

## 期待される利益

- **再利用性**: どのコンポーネントも Timeline 依存なしでジョイント階層を作成可能
- **テスト容易性**: ユーティリティクラスを単独でユニットテスト可能
- **保守性**: 階層作成ロジックの単一の情報源
- **モジュラリティ**: Timeline 再生ロジックが階層作成から分離
- **拡張性**: 将来新しいファクトリメソッドや作成戦略を追加しやすい

## 実装順序

1. `BvhJointHierarchyBuilder.cs` を完全実装で作成
2. `BvhPlayableBehaviour.cs` を新しいユーティリティを使用するよう更新
3. コンパイル確認と Timeline 再生テスト
4. ドキュメント更新

## テスト戦略

- Timeline 再生が正しく機能することを確認
- 正しい transform プロパティ（offset、rotation、scale）で階層が作成されることをテスト
- Idempotency テスト（複数回呼び出しで GameObjects が重複しない）
- 階層作成後スケルトン視覚化が表示されることを確認
- 作成された階層でフレーム適用が機能することをチェック
