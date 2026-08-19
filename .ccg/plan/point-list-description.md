## 实施计划：点列表显示工程描述

### 任务类型

- [x] 前端
- [x] 后端
- [x] 全栈

### 增强后的需求

在管理台 `/#/browse?tab=points` 的点列表中新增“描述”列。描述必须来自当前已装载工程的 `Cfg_VarSystem.Description` 字段，并随工程重新装载/在线下装后的当前代元数据同步更新。

范围边界：

- 扩展 `GET /api/points` 的每个 `items[]` 元素，新增小写驼峰字段 `description`。
- 点列表在“点名”之后展示“描述”列。
- `Cfg_VarSystem` 中没有对应记录的运行时中间点返回 `description: null`，页面显示 `—`。
- 本次不修改点名搜索语义；参数 `q` 仍只匹配点名。
- 本次不修改 MDB 表结构、`PointModel`、`PointSlot`/Arena 内存布局、点详情页或收藏页。

### 已核对的数据链路

```text
Cfg_VarSystem.Description
    ↓ MdbEngineeringReader 查询并映射
PointModel.Description
    ↓ RuntimeHost.BuildPointMetadataIndex
_pointMetadataByDpu[DPU][点名]
    ↓ RuntimeHost.TryGetPointModel（O(1)）
GET /api/points → items[].description
    ↓ app.js/renderPoints
点列表“描述”列
```

现有代码已经完成前半段数据读取和索引构建：

- `MdbEngineeringReader` 的 SQL 已查询 `Cfg_VarSystem.Description`，并映射到所有支持点类型的 `PointModel.Description`。
- `PointModel` 已定义 `Description`。
- `RuntimeHost` 已按 DPU 和点名构建大小写不敏感的只读元数据索引；工程换代时会整体替换该索引。
- `/api/points` 已持有当前代 `RuntimeReadLease`，元数据查询与运行时点遍历可以保持在同一工程代内。

因此无需新增数据库访问或模型字段，只需要消费现有元数据。

### 方案对比

| 方案 | 优点 | 缺点 | 结论 |
|------|------|------|------|
| `/api/points` 使用 `RuntimeHost.TryGetPointModel(dpu, point)` 补充描述 | O(1) 查询；只处理当前页；复用现有索引；与热重载同步；前端一次请求即可完成 | API 响应每项增加一个字符串字段 | 推荐 |
| 前端为每行再请求点详情或其他接口 | 后端列表接口不变 | 产生 N+1 请求；首屏闪烁；分页 50 条时开销明显；现有点详情也未直接提供该字段 | 不采用 |
| 将描述写入 `PointSlot`/Arena | 运行时点对象可直接读取 | 描述不是实时值；增加非托管布局复杂度和内存；需要改布局、构建器及兼容协议，风险远超需求 | 不采用 |
| 每次请求扫描 `PristineModel.Controllers[].Points` | 修改量表面较小 | 单条元数据关联退化为线性扫描；大工程 12 万点以上时不合适 | 不采用 |

### 技术方案

推荐在 `/api/points` 已通过筛选且进入当前页的分支内调用一次 `_host.TryGetPointModel(d.Name, name, out var pointModel)`，把 `pointModel.Description` 写入匿名响应对象的 `description` 字段。

该查询放在分页命中分支内，而不是对所有点执行，可使一页 50 条时最多进行 50 次字典查询。查不到元数据时返回 `null`，覆盖运行时自动生成点或中间点。前端统一用已有 `esc()` 转义描述文本，并用 CSS 单行省略；完整内容放入 `title`，鼠标悬停可查看，避免长描述撑宽表格。

### 实施步骤

1. 扩展点列表 API — 在 `GET /api/points` 当前页对象中加入 `description`，数据通过现有 O(1) 元数据索引获取。
2. 扩展点列表表头与行模板 — 在“点名”之后加入“描述”，对空值显示 `—`，对文本同时进行元素内容和 `title` 属性转义。
3. 添加描述列样式 — 设置合理的最小/最大宽度、单行截断和省略号，保持当前宽屏表格布局稳定。
4. 扩展 API 契约测试 — 构造带描述的 `PointModel`，访问 `/api/points`，验证对应点返回预期 `description`。
5. 执行自动化与手工验证 — 先运行目标测试和完整测试，再在 5100 端口页面验证描述列、空描述与长描述。

### 关键文件

| 文件 | 操作 | 说明 |
|------|------|------|
| `src/Api/RWVDCS.Api/ApiServer.cs:219` | 修改 | 扩展 `GET /api/points` 的 `items[]`，加入 `description` |
| `src/Api/RWVDCS.Api/wwwroot/app.js:325` | 修改 | 增加“描述”表头和单元格渲染 |
| `src/Api/RWVDCS.Api/wwwroot/styles.css:121` | 修改 | 增加描述列宽度、截断和悬停查看所需样式 |
| `src/Tests/RWVDCS.Runtime.Tests/ApiValueContractTests.cs:13` | 修改 | 扩展现有 API 集成测试，验证 `/api/points` 描述契约 |

只读核对文件，无需修改：

- `src/Engineering/RWVDCS.Engineering/MdbEngineeringReader.cs`
- `src/Engineering/RWVDCS.Engineering/EngineeringModel.cs`
- `src/Api/RWVDCS.Api/RuntimeHost.cs`

### 建议修改代码

#### 1. `ApiServer.cs`：返回 `description`

在 `/api/points` 的分页命中分支中，用以下代码替换当前 `items.Add(...)`：

```csharp
if (total > skip && items.Count < pageSize)
{
    string? description = _host.TryGetPointModel(d.Name, name, out var pointModel)
        ? pointModel.Description
        : null;

    items.Add(new
    {
        dpu = d.Name,
        name,
        description,
        kind = slot.Kind.ToString(),
        value = slot.ReadBoxedBuffer(),
        forced = IsPointForced(slot),
    });
}
```

预期 API 响应示例：

```json
{
  "total": 123692,
  "page": 1,
  "pageSize": 50,
  "items": [
    {
      "dpu": "DPU1001",
      "name": "10PAY03DI001",
      "description": "循环水泵C出口液控蝶阀远程",
      "kind": "LD",
      "value": false,
      "forced": false
    }
  ]
}
```

#### 2. `app.js`：增加表头

把 `renderPoints()` 内的表头改为：

```javascript
<table class="grid"><thead><tr>
  <th style="width:30px"></th><th>点名</th><th class="point-description">描述</th><th>类型</th><th>DPU</th><th class="num">当前值</th><th>强制</th><th></th>
</tr></thead><tbody id="rows"></tbody></table>
```

#### 3. `app.js`：增加描述单元格

把点名行后半部分改为：

```javascript
$("#rows").innerHTML = data.items.map(p => {
  const description = String(p.description ?? "").trim();
  const descriptionHtml = description
    ? esc(description)
    : '<span class="dim">—</span>';

  return `<tr>
    <td><span class="fav ${favStore.hasPoint(p.name) ? "on" : ""}" data-name="${esc(p.name)}">★</span></td>
    <td class="mono"><a class="link" href="#/pointinfo?type=point&name=${encodeURIComponent(p.name)}">${esc(p.name)}</a></td>
    <td class="point-description" title="${esc(description)}">${descriptionHtml}</td>
    <td>${p.kind}</td><td>${esc(p.dpu)}</td>
    <td class="num">${fmtVal(p.value)}</td>
    <td>${p.forced ? '<span class="badge badge-warn">强制</span>' : ""}</td>
    <td><a class="link" href="#/pointinfo?type=point&name=${encodeURIComponent(p.name)}">PointInfo →</a></td>
  </tr>`;
}).join("");
```

说明：描述来自工程库，仍必须使用 `esc()`，避免 MDB 中的 `<`、`&`、引号等字符破坏页面结构或造成 HTML 注入。`title` 属性也必须转义。

#### 4. `styles.css`：限制描述列宽度

在表格通用样式附近加入：

```css
table.grid th.point-description,
table.grid td.point-description {
  min-width: 180px;
  max-width: 420px;
}

table.grid td.point-description {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
```

#### 5. `ApiValueContractTests.cs`：验证接口契约

在测试模型的 `AI001` 上加入：

```csharp
Description = "给水流量",
```

在服务器启动后的现有集成测试中加入：

```csharp
using HttpResponseMessage pointsResponse = await client.GetAsync(
    "/api/points?dpu=DPU1&page=1&pageSize=50");

Assert.Equal(HttpStatusCode.OK, pointsResponse.StatusCode);
using JsonDocument pointsJson = JsonDocument.Parse(
    await pointsResponse.Content.ReadAsStreamAsync());
JsonElement pointItem = Assert.Single(
    pointsJson.RootElement.GetProperty("items").EnumerateArray(),
    item => item.GetProperty("name").GetString() == "AI001");
Assert.Equal("给水流量", pointItem.GetProperty("description").GetString());
```

可将测试名从：

```csharp
Value_endpoints_return_engineering_metadata_and_compatibility_status_fields
```

调整为：

```csharp
Value_and_points_endpoints_return_engineering_metadata_and_compatibility_status_fields
```

### 风险与缓解

| 风险 | 缓解措施 |
|------|----------|
| 运行时中间点不存在 `PointModel` 元数据 | `TryGetPointModel` 失败时返回 `null`，前端显示 `—`，不让整个接口失败 |
| 描述过长导致表格横向膨胀 | 描述列设置最大宽度、单行省略号；`title` 保留完整查看能力 |
| 描述包含 HTML 或引号 | 单元格正文和 `title` 均使用现有 `esc()` 转义 |
| 大工程列表性能下降 | 仅对当前页最多 `pageSize` 条记录做 O(1) 字典查询，不扫描 `PristineModel`，不发额外 SQL/HTTP 请求 |
| 工程热重载时描述与点值跨代 | `/api/points` 已持有 `RuntimeReadLease`，元数据索引随 Runtime 换代整体更新 |
| 浏览器缓存旧 `app.js` | 部署/重启后使用 `Ctrl+F5` 强制刷新进行联调；如部署链路长期缓存静态资源，再单独增加静态资源版本策略 |
| 当前工作区已有未提交的 Runtime 生命周期修复 | 实施时只在上述目标代码块增量修改，保留现有未提交内容，不覆盖或回滚无关改动 |

### 验收标准

- [ ] `GET /api/points?...` 的每个返回项都包含 `description` 字段。
- [ ] `Cfg_VarSystem.Description` 有值的点，接口返回内容与工程数据库一致。
- [ ] 无描述或无 `Cfg_VarSystem` 元数据的点返回 `null`/空值，页面稳定显示 `—`。
- [ ] `/#/browse?tab=points` 的“点名”后出现“描述”列。
- [ ] 长描述不撑破表格，鼠标悬停可看到完整内容。
- [ ] 描述中的 `<`、`>`、`&`、单双引号按文本显示，不作为 HTML 执行。
- [ ] 点名搜索、类型过滤、DPU 过滤、分页、收藏、PointInfo 跳转和强制状态显示保持原行为。
- [ ] 工程重新装载后列表显示当前工程的描述，而非上一代缓存。

### 验证方式

1. 目标测试：

   ```powershell
   dotnet test src/Tests/RWVDCS.Runtime.Tests/RWVDCS.Runtime.Tests.csproj --filter "FullyQualifiedName~ApiValueContractTests"
   ```

2. 完整回归：

   ```powershell
   dotnet test RWVDCS.sln
   ```

3. API 冒烟检查：

   ```powershell
   Invoke-RestMethod 'http://localhost:5100/api/points?q=10PAY03DI001&page=1&pageSize=50' |
     ConvertTo-Json -Depth 6
   ```

4. 浏览器验证：访问 `http://localhost:5100/#/browse?tab=points`，搜索一个已知有描述的点，再检查空描述和长描述点；部署到 `192.168.1.135` 后用 `Ctrl+F5` 刷新并重复验证。

