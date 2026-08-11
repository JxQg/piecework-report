# 计件工资管理

面向局域网使用的 ASP.NET Core 8 Razor Pages 应用，采用 SQLite 存储并通过 ClosedXML 导入、导出 Excel。Windows 正式版由 WinForms 启动器管理服务、端口、局域网分享、账号、托盘、开机启动及离线升级。文员负责员工、机器、物料、规格与每日计件；经理独占预算、计价、工资增项、工资查看和报表导出。

## 工资算法

- 规格扣数表示每件产品的计薪工作量，机器只控制规格适配与计价上下文。
- 实际扣数 = 完成件数 × 规格扣数。
- 日达标率贡献 = 实际扣数 ÷ 员工达标扣数。
- 每扣单价 = 规则达标工资 ÷ 员工达标扣数。
- 每件单价 = 每扣单价 × 规格扣数。
- 明细工资 = 完成件数 × 每件单价。
- 直接单价模式按配置的每件单价计薪，达标率仍按员工达标扣数计算。
- 明细和日工资保留完整精度；员工月计件工资在月末统一四舍五入一次，再累加工资增项。
- 上级预算只用于给出建议达标工资和观察偏差，不限制或二次分配实际工资。

## 权限

| 角色 | 用户名 | 权限 |
| --- | --- | --- |
| 经理 | `manager` | 全部功能，包括所有金额、计价、增项、报表和历史文件 |
| 文员 | `clerk` | 员工、机器、物料、规格、计件录入及不含金额的 Excel 导入 |

正式版不提供默认密码。首次打开启动器时必须为 `manager` 和 `clerk` 设置至少 8 位且同时包含字母和数字的密码；之后由启动器修改经理密码或重置文员密码。

## 开发运行

需要 .NET 8 SDK：

```powershell
dotnet restore PieceworkReport.sln
dotnet test PieceworkReport.sln --no-restore
dotnet run --project src/PieceworkReport.Web/PieceworkReport.Web.csproj --no-restore
```

开发运行 Web 项目时默认访问 `http://localhost:5188`。正式使用请安装发布安装包并从启动器进入；启动器会显示本机与局域网地址。

## 使用流程

1. 经理创建工资月份，配置预算、人数及实际工作日期。
2. 文员维护员工、机器、物料和物料规格，并配置机器允许加工的规格。
3. 经理按月份和机器规格配置达标工资、默认达标扣数或直接单价，并按需覆盖员工达标扣数。
4. 文员逐日录入，或下载模板后预览并整批导入员工、计件或规格数据。
5. 经理添加工资增项、检查偏差并导出员工工资表。

导入以编码为准。员工、规格和计件导入均先预览，任一行重复、非法、未知或缺少配置时整批不写入。工资导出按员工生成 Sheet，并保留每次导出的版本快照。

## 示例数据

`tools/PieceworkReport.DemoBuilder` 从原报表前三个 Sheet 创建独立示例数据库，并校验 15 类物料、262 个规格、27 个工作日、456 条计件明细和计薪结果：

```powershell
dotnet run --project tools/PieceworkReport.DemoBuilder/PieceworkReport.DemoBuilder.csproj -- `
  "2026年7月份计件表7.31.xlsx" "artifacts/demo-build" --force
```

示例机器统一标记为“原表未记录机器”，不会推断设备或工资增项。示例数据只用于开发回归，不进入正式安装包。

## Windows 安装与数据目录

- 程序：`C:\Program Files\PieceworkReport`
- 数据库：`C:\ProgramData\PieceworkReport\data\piecework-report.db`
- 自动及导入前备份：`C:\ProgramData\PieceworkReport\data\backups\`
- 临时导入：`C:\ProgramData\PieceworkReport\data\imports\`
- 工资表快照：`C:\ProgramData\PieceworkReport\data\exports\`
- 启动器日志：`C:\ProgramData\PieceworkReport\logs\`

正式安装包不包含业务数据库。升级只替换 Program Files 中的程序文件，数据库迁移前自动备份；卸载默认保留 ProgramData 中的正式数据。

## 发布安装包

需要 .NET SDK 和 Inno Setup 6：

```powershell
pwsh -NoProfile -File packaging/build-release.ps1 -Version 2.1.0
```

输出为 `artifacts/installer/PieceworkReport-Setup-2.1.0.exe`。安装器面向 Windows x64、需要管理员授权，并为专用网络添加 Web 入站防火墙规则。
