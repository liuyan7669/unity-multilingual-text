# Multilingual Text

适用于 Unity 的多语言文本，融合多语言文本与提示文本配置，支持外部查询、语言切换、提示音频路径和 `Text`/`TMP_Text` 自动刷新。

## 功能

- 使用一份 JSON 配置多条记录及其各语言文本和提示音频路径。
- 支持当前语言、回退语言、语言记忆和运行时切换。
- 提供 `Text` 与 `TMP_Text` 自动监听刷新组件。
- 提供可视化编辑器，支持搜索替换、数据校验、Undo/Redo 和未保存状态恢复。
- 提供 Manager 与 Listener 专用 Inspector，支持配置文件诊断、词条预览和快捷操作。
- 支持通过 Hierarchy 右键菜单定位或创建多语言文本。
- 支持百度翻译 API 配置、单语言翻译、单记录翻译和整表翻译。
- 翻译任务使用 Unity 可取消进度条，可随时停止。
- 支持通过 `UnityWebRequest` 从 `StreamingAssets` 异步加载配置。

## 安装

### 通过 OpenUPM 安装

在 Unity 的 `Edit > Project Settings > Package Manager` 中添加：

```text
Name: package.openupm.com
URL: https://package.openupm.com
Scope: com.cowart.multilingual-text
```

然后通过 `Add package by name` 安装：

```text
com.cowart.multilingual-text
```

### 通过 Git URL 安装

```text
https://github.com/liuyan7669/unity-multilingual-text.git#1.0.5
```

## 创建示例配置

配置文件默认位于 `Assets/StreamingAssets/multilingual_text.json`：

```json
{
  "nextId": 2,
  "items": [
    {
      "id": 1,
      "translations": [
        {
          "language": 0,
          "content": "第一段提示。\n第二段提示。",
          "audioPath": "Audio/Tips/Chinese/tip_001"
        },
        {
          "language": 1,
          "content": "First tip.\nSecond tip.",
          "audioPath": "Audio/Tips/English/tip_001"
        }
      ]
    }
  ]
}
```

- `id`：大于 0 的全局唯一整数 ID。编辑器新增记录时使用 `nextId` 自动递增，删除记录不会重排或复用已有 ID。
- `nextId`：编辑器下次自动分配的 ID；手动输入更大的 ID 后会自动向后推进。
- `language`：`SupportedLanguage` 的整数值。
- `content`：当前语言的完整文本内容，可直接包含换行符。
- `audioPath`：当前语言的提示音频 `Resources` 相对路径，可为空。

可通过 `Cowart > 语言管理 > 创建示例配置` 创建示例文件，已有同名文件时不会覆盖。

## 使用方法

### 编辑多语言配置

通过 `Cowart > 语言管理 > 打开编辑器`，或在 `MultilingualTextManager` Inspector 中打开可视化编辑器。编辑器支持：

- 表格视图与详情视图，并记住上次使用的视图。
- 搜索、查找下一个、当前替换和全部替换。
- 新增、复制、删除、Undo/Redo 和未保存状态提示。
- 多语言文本内容和各语言音频路径编辑。
- 空 ID、重复 ID、重复语言和缺少文本的数据校验。
- 选择、重载、保存、定位现有文件，以及创建新的示例文件。
- 从编辑器工具栏打开独立百度翻译配置窗口，并在详情视图翻译单条记录，或从顶部工具栏翻译整表；只生成已经添加的目标语言，并在调用前预估费用。

### 场景初始化

在 Hierarchy 中右键选择 `Cowart > 语言管理 > 创建多语言文本`。场景中已存在 `MultilingualTextManager` 时会直接选中并高亮，避免重复创建；不存在时会在场景根节点创建 `[多语言文本]`。

Manager Inspector 会检查 `File Name` 对应的 `StreamingAssets` 配置文件，并可直接打开当前配置、创建默认配置或定位文件。管理器会读取该配置，切换场景时保持存在。

### 手动查询

```csharp
using Cowart.MultilingualText;
using TMPro;

public TMP_Text targetText;

private void Refresh()
{
    targetText.text = MultilingualTextManager.GetText(1);
    string audioPath = MultilingualTextManager.GetAudioPath(1);
}
```

切换语言：

```csharp
MultilingualTextManager.CurrentLanguage = SupportedLanguage.English;
```

重新加载可调用 `MultilingualTextManager.Reload()`；停止当前加载可调用 `MultilingualTextManager.StopLoading()`。

### 自动刷新文本

将 `MultilingualTextListener` 添加到带有 `Text` 或 `TMP_Text` 的同一个 GameObject，并填写文本 ID。语言切换或配置加载完成后，组件会使用完整 `content` 自动刷新。

Listener Inspector 会按照当前场景 Manager 的 `File Name` 显示词条内容预览，并在缺少文本组件时提供快捷添加入口。

- `Text Id = 0`：未绑定任何多语言内容，新添加组件时的默认状态。
- `Text Id > 0`：绑定 `multilingual_text.json` 中对应的内容 ID。
- `Clear Text When Missing`：配置加载完成但找不到对应 ID 时，决定清空文本还是保留当前内容。

## 主要类型

- `MultilingualTextManager`：加载配置、切换语言并提供文本和音频路径查询。
- `MultilingualTextListener`：监听语言和配置变化，自动刷新同一 GameObject 上的 `Text` 或 `TMP_Text`。
- `MultilingualTextConfig`：读取、重新读取或停止读取 JSON 配置。
- `SupportedLanguage`：定义系统支持的语言。

## 注意事项

- `id` 必须是大于 0 的唯一整数；`0` 专门表示监听组件尚未绑定记录。
- `audioPath` 保存的是 `Resources` 相对路径，不包含 `Resources` 目录名和文件扩展名。
- 百度翻译的 App ID 和密钥由编辑器翻译配置窗口管理，不应提交真实密钥到公开仓库。
- 发布新版本时，请同步更新 `package.json` 中的 `version`、README 安装地址和 `CHANGELOG.md`。

## 环境要求

- Unity 2021.3 或更高版本
- TextMeshPro 3.0.6

## 许可证

本包使用 MIT License，详情参见 `LICENSE.md`。
