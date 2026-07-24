# 贡献指南 / Contributing

感谢你的兴趣！本项目是个人维护的小工具，欢迎 issue 和 PR。以下是协作约定。

## 在开始写代码之前

- **较大的改动请先开 issue 讨论方向**，达成一致再动手，避免做了半天被婉拒。
- 小而明确的 bug 修复可以直接发 PR。

## 构建与验证

需要 .NET SDK 10（Windows）：

```powershell
# 构建
dotnet build app/AIQuotaMonitor.csproj -c Release

# 发布（自包含单文件 exe）
dotnet publish app/AIQuotaMonitor.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/
```

**改动必须自验证**，本项目有离线截图模式（不连真实网站、不需要登录态）：

```powershell
# 主窗口（示例数据：正常/需要登录/抓取失败三态，含基准线、订阅行、主题色）
AIQuotaMonitor.exe --test-shot out.png --lang zh        # 中文版
AIQuotaMonitor.exe --test-shot out_en.png --lang en     # 英文版
AIQuotaMonitor.exe --test-shot out_h.png --layout horizontal

# 设置窗口（两个 Tab 各出一张图）
AIQuotaMonitor.exe --test-settings-shot settings
```

UI 改动请在 PR 里附上改动前后的截图对比。

## 代码约定

- **最小改动**：只改与目的相关的代码，不顺手重构/重排格式。
- **多语言**：所有用户可见字符串必须进 `app/I18n.cs` 的**中英两张表**（key 蛇形命名，两表 key 保持一致），不允许硬编码 UI 文案。中文注释保持中文。
- **抓取逻辑**（ScrapeEngine）：站点相关的特殊处理要写成通用机制（参考已有的 iframe 回退、质量轮询、字号启发式），不要为单一站点硬编码。
- **隐私红线**：不得引入任何把用户数据（Cookie、配置、页面内容）传出本机的代码；Cookie 只能 DPAPI 加密存本地（见 `CookieStore.cs`）。

## PR 流程

1. Fork 后在特性分支上开发，提交信息用英文、祈使句（如 `fix: axis label picked as value before data renders`）。
2. PR 描述写清：问题/动机、方案、验证方式（截图或日志）。
3. CI（build workflow）必须绿；仓库所有者 review 后合并（通常用 squash）。

## 行为准则

友善、对事不对人。维护者时间有限，回复可能不及时，请见谅。
