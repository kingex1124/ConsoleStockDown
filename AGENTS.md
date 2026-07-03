# Repository Guidelines

## 專案結構與模組配置
Solution 入口是 [`ConsoleStockDown.slnx`](./ConsoleStockDown.slnx)。主要程式碼位於 `ConsoleStockDown/`：`Program.cs` 負責 Host、設定、DI 與 logging 組態；`Services/` 處理 TWSE 資料抓取與轉換流程；`Repository/` 與 `DataAccess/` 負責 SQLite 存取；`Models/` 定義資料表實體；`Logging/` 放置自訂檔案 logger；`Configuration/` 存放強型別設定；`Doc/` 保存資料庫結構說明。`stockdata.db`、`bin/`、`obj/` 與執行時產生的 `logs/` 都屬於輸出物，不應視為原始碼。

## 建置、測試與開發指令
除非另有註明，請從 workspace root 執行以下指令。

- `dotnet restore ConsoleStockDown/ConsoleStockDown.csproj`：還原 NuGet 套件。
- `dotnet build ConsoleStockDown/ConsoleStockDown.csproj`：建置 `net10.0` 主控台程式。
- `dotnet run --project ConsoleStockDown/ConsoleStockDown.csproj`：下載最新 TWSE 日資料並寫入 SQLite 與 log。
- `dotnet clean ConsoleStockDown/ConsoleStockDown.csproj`：清除建置輸出，方便重新驗證。

## 程式風格與命名慣例
沿用目前專案中的標準 C# 風格：4 個空白縮排、file-scoped namespace、啟用 nullable reference types，非同步方法以 `Async` 結尾。型別與成員名稱使用 PascalCase，介面以 `I` 開頭，區域變數使用 camelCase，私有欄位使用前置底線。資料解析邏輯優先放在 `Services/`，資料存取邏輯維持在 `Repository/`。執行紀錄請使用 `ILogger<T>`，不要零散加入 `Console.WriteLine`。

## Commit 與 Pull Request 指引
近期提交紀錄多使用精簡的繁體中文 commit subject，直接描述變更內容，例如 `新增資料庫架構文件`、`重構 FileLogger 類別`。每個 commit 應保持單一目的，避免使用過度籠統的訊息。Pull request 需摘要說明行為變更、列出主要修改路徑、註明是否影響 `appsettings.json` 或資料庫行為，並附上實際使用的驗證指令。這是主控台專案，通常不需要截圖；若有行為差異，附上 console 或 log 範例會更有幫助。

## 設定與資料注意事項
`ConsoleStockDown/appsettings.json` 定義 API URL、資料庫檔名與 log 路徑。環境相關覆寫請保留在本機，不要提交已寫入資料的資料庫檔案或執行產生的 logs。目前 `.gitignore` 只排除了 `bin/` 與 `obj/`，因此推送前請仔細檢查 staged files。
