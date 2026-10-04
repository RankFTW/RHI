# 界面语言

在 **设置 → 语言** 中选择 **英语** 或 **简体中文**。标题和语言选项只显示当前界面语言的名称，并随语言切换立即更新。
选择保存在现有的 `%LocalAppData%\RHI\settings.json` 的 `InterfaceLanguage` 字段中，切换后立即生效，无需重启。首次启动根据 Windows 界面语言选择中文或英文；无效的已保存语言回退到英文。

应用自己的主界面、设置、游戏详情、快速入门、弹窗、托盘菜单和操作状态支持中文。组件名称、游戏名称、路径、DLL 文件名和写入游戏的配置值保留原文。远程发布的更新日志、Wiki 说明、每日消息以及第三方错误详情保留来源语言。

## 维护翻译

- 静态 XAML 文本绑定到应用资源 `LocalizedStrings`（`XamlCatalog`），文本仍维护在 `Strings/en-US/Resources.resw` 和 `Strings/zh-CN/Resources.resw`。英文和中文文件必须包含相同的资源键；绑定别名为 `控件资源标识_属性名`。
- 代码创建的文本使用 `.Localize("Text", Loc.Get(...))` 等实时绑定。不要直接覆盖这些显示属性；通过 `.Localize` 更新，才能继续响应语言变化。长提示拼接使用 `Loc.Concat(...)` 保留各部分的翻译来源。
- 无安装包版本在 `App` 构造函数中、`InitializeComponent()` 之前设置 `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride`。不要改用需要包标识的 `Windows.Globalization` 同名接口，否则会在启动时崩溃。
- 代码中的界面字符串使用 `Loc.Get("English text")`，翻译维护在 `Localization/zh-CN.json`。
- 包含变量的提示使用 `Loc.Format($"English {value}")`，字典键使用 .NET 复合格式占位符，例如 `English {0}`。保留变量、格式说明和必要的换行。中文可省略只用于英文复数的后缀参数。
- 下拉列表继续使用英文 `SelectedItem`、`Tag` 和配置值，通过 `LocalizedTextConverter` 翻译显示标签。不要翻译用于分支判断的字符串、驱动标识符、INI 参数或 DLL 版本。
- 展示游戏名称、自定义预设名称等用户数据的下拉列表应设置 `ItemTemplate = null`；自带 `ComboBoxItem` 同时设置 `ContentTemplate = null`。使用 `DisplayMemberPath` 的对象列表也应清除默认翻译模板。
- 新增文本没有翻译时保留英文。添加翻译后运行 `LocalizationTests`，检查格式占位符和语言保存行为。
