# ConsoleStockDown

`ConsoleStockDown` 是一個以 .NET 開發的主控台程式，會從台灣證券交易所公開 API 下載每日個股資料與三大法人買賣資料，將資料寫入本機 SQLite，並計算相對前一交易日的漲跌幅，方便後續查詢、分析或擴充成排程服務。

## 功能特色

- 呼叫 TWSE OpenAPI 取得當日上市股票日資料
- 呼叫 TWSE `T86` API 取得三大法人買賣超日報
- 自動建立 SQLite `StockDaily` 資料表
- 自動建立 SQLite `InstitutionalTradeDaily` 資料表
- 將 API 日期統一轉成 `yyyy-MM-dd`
- 依前一交易日收盤價計算 `ChangeRate`
- 三大法人 API 會沿用最新日線交易日組出 `date` 參數，確保資料日期一致
- 可用 `appsettings.json` 覆寫三大法人抓取日期，方便補抓指定交易日
- 三大法人 API 若回應異常會自動重試 3 次，並在 log 記錄 `stat` 與回應片段
- 寫入前先刪除同交易日舊資料，避免重複
- 同時輸出 Console 與檔案日誌
- 日誌檔依日期分檔，例如 `stock-service-2026-07-01.log`

## 執行流程

程式啟動後會依序執行以下步驟：

1. 讀取 `appsettings.json`
2. 初始化 SQLite 資料表
3. 呼叫 `https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL`
4. 解析每筆股票資料並轉換欄位格式
5. 查詢資料庫中的前一交易日資料
6. 以「前一交易日收盤價」計算當日 `ChangeRate`
7. 刪除當前交易日既有 `StockDaily` 資料後重新寫入
8. 若 `InstitutionalTradeFetchDate` 有設定則使用該日期，否則以最新 `StockDaily` 交易日組成 TWSE `T86` 的 `date` 參數
9. 呼叫 `https://www.twse.com.tw/rwd/zh/fund/T86`
10. 若三大法人 API 回應異常則自動重試，並在 log 記錄 `stat` 與回應摘要
11. 解析三大法人買賣資料並寫入 `InstitutionalTradeDaily`
12. 將執行結果寫入 Console 與日誌檔

## 技術棧

- .NET 10
- C#
- SQLite
- Linq To DB
- Microsoft.Extensions.Hosting
- Microsoft.Extensions.Logging

## 專案結構

```text
ConsoleStockDown/
├─ ConsoleStockDown.slnx
├─ README.md
└─ ConsoleStockDown/
   ├─ Configuration/
   │  └─ AppSettings.cs
   ├─ DataAccess/
   │  └─ AppDataConnection.cs
   ├─ Doc/
   │  └─ DatabaseSchema.md
   ├─ Logging/
   │  ├─ FileLogger.cs
   │  └─ FileLoggerProvider.cs
   ├─ Models/
   │  ├─ InstitutionalTradeDaily.cs
   │  └─ StockDaily.cs
   ├─ Repository/
   │  ├─ IInstitutionalTradeRepository.cs
   │  ├─ IStockRepository.cs
   │  ├─ InstitutionalTradeRepository.cs
   │  └─ StockRepository.cs
   ├─ Services/
   │  ├─ IInstitutionalTradeService.cs
   │  ├─ IStockService.cs
   │  ├─ InstitutionalTradeService.cs
   │  └─ StockService.cs
   ├─ Program.cs
   ├─ ConsoleStockDown.csproj
   ├─ appsettings.json
   └─ stockdata.db
```

## 系統需求

- .NET SDK 10.0 或以上版本
- 可連線至 TWSE OpenAPI
- Windows、Linux、macOS 均可執行，前提是已安裝相容的 .NET SDK

## 快速開始

### 1. 還原套件

```powershell
dotnet restore .\ConsoleStockDown\ConsoleStockDown.csproj
```

### 2. 建置專案

```powershell
dotnet build .\ConsoleStockDown\ConsoleStockDown.csproj
```

### 3. 執行程式

```powershell
dotnet run --project .\ConsoleStockDown\ConsoleStockDown.csproj
```

## 設定說明

設定檔位於 `ConsoleStockDown/appsettings.json`。

```json
{
  "AppSettings": {
    "DatabaseFileName": "stockdata.db",
    "ApiUrl": "https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL",
    "InstitutionalTradeApiUrlTemplate": "https://www.twse.com.tw/rwd/zh/fund/T86?date={date}&selectType=ALL&response=json",
    // Optional: specify a date to fetch institutional trade data.
    // Leave null to use the latest StockDaily trade date, which is usually the previous trading day.
    // Supported formats: "2026-07-02" or "20260702".
    "InstitutionalTradeFetchDate": null,
    "LogFilePath": "logs/stock-service.log"
  }
}
```

### AppSettings 欄位

| 欄位 | 說明 |
| --- | --- |
| `DatabaseFileName` | SQLite 檔案名稱。程式會將它放在專案目錄下。 |
| `ApiUrl` | 股票日資料 API 位址。 |
| `InstitutionalTradeApiUrlTemplate` | 三大法人 API URL 範本，必須包含 `{date}` 佔位符，程式會以最新股票交易日轉成 `yyyyMMdd` 後代入。 |
| `InstitutionalTradeFetchDate` | 可選的三大法人抓取日期。支援 `yyyy-MM-dd` 與 `yyyyMMdd`，若為 `null` 或未設定，會改抓最新 `StockDaily` 交易日。 |
| `LogFilePath` | 日誌基底路徑。實際輸出時會自動加上日期，例如 `logs/stock-service-2026-07-01.log`。 |

## 資料輸出位置

### 資料庫

- 預設檔名：`stockdata.db`
- 預設位置：`ConsoleStockDown/stockdata.db`
- 第一次執行時若不存在會自動建立

### 日誌

- 日誌會寫到執行目錄下的 `logs/`
- 檔名格式：`stock-service-yyyy-MM-dd.log`
- 例如：`logs/stock-service-2026-07-01.log`

## 資料表說明

目前資料庫包含兩張主要資料表：

- `StockDaily`
- 交易日期 `TradeDate`
- 股票代號 `StockCode`
- 股票名稱 `StockName`
- 成交股數 `TradeVolume`
- 成交金額 `TradeValue`
- 開盤、最高、最低、收盤價
- 漲跌價差 `PriceChange`
- 成交筆數 `TransactionCount`
- 漲跌幅 `ChangeRate`

- `InstitutionalTradeDaily`
- 交易日期 `TradeDate`
- 股票代號 `StockCode`
- 股票名稱 `StockName`
- 外資、投信、自營商的買進、賣出、買賣超股數
- 三大法人合計買賣超股數 `InstitutionalInvestorsNet`

完整欄位定義可參考 `ConsoleStockDown/Doc/DatabaseSchema.md`。

## 日期與漲跌幅處理規則

- 若 API 日期為 `yyyyMMdd`，會轉成 `yyyy-MM-dd`
- 若 API 日期為民國格式，例如 `1140701`，會轉成西元日期
- 若有設定 `InstitutionalTradeFetchDate`，三大法人資料會使用該日期查詢 `T86` API
- 若未設定 `InstitutionalTradeFetchDate`，三大法人資料會以最新 `StockDaily.TradeDate` 轉成 `yyyyMMdd` 查詢 `T86` API
- `ChangeRate` 計算方式為：

```text
(當日收盤價 - 前一交易日收盤價) / 前一交易日收盤價 * 100
```

- 若找不到前一交易日資料，`ChangeRate` 會保留為 `NULL`

## 主要元件說明

- `Program.cs`
  應用程式入口，負責建立 Host、載入設定、註冊 DI 與 Logging，並依序執行股票日資料與三大法人資料同步。
- `Services/StockService.cs`
  實作資料抓取、欄位轉換、漲跌幅計算與資料寫入流程。
- `Services/InstitutionalTradeService.cs`
  實作三大法人買賣資料抓取、日期參數轉換、欄位解析與資料寫入流程。
- `Repository/StockRepository.cs`
  封裝 SQLite 存取邏輯，包含建表、查詢、刪除與新增。
- `Repository/InstitutionalTradeRepository.cs`
  封裝 `InstitutionalTradeDaily` 的建表、刪除與新增邏輯。
- `Logging/FileLogger.cs`
  自訂檔案日誌輸出，依日期建立不同 log 檔。
- `Models/StockDaily.cs`
  定義 `StockDaily` 資料表欄位與對應模型。
- `Models/InstitutionalTradeDaily.cs`
  定義 `InstitutionalTradeDaily` 資料表欄位與對應模型。

## 注意事項

- 程式每次執行都會以 API 最新交易日資料覆寫該交易日的既有資料。
- 三大法人資料依賴 `StockDaily` 的最新交易日來決定 `T86` API 的 `date` 參數，因此執行順序固定為先抓日線、再抓法人。
- 若 `InstitutionalTradeFetchDate` 設定格式錯誤，程式會直接拋出設定錯誤，避免誤抓資料。
- 若 `T86` API 暫時回傳異常內容，程式會自動重試 3 次，並把 `stat` 與回應片段寫入 log 方便排查。
- `InsertStocksAsync` 目前逐筆寫入，若未來資料量或執行頻率提高，可考慮改成批次寫入。
- `InsertInstitutionalTradesAsync` 目前也採逐筆寫入，若後續資料量增加可再改成批次寫入。
- 若 API 回傳欄位格式變動，`StockService` 與 `InstitutionalTradeService` 的解析邏輯需要同步調整。

## 後續可擴充方向

- 增加排程執行
- 補上單元測試與整合測試
- 增加 CLI 參數，例如指定日期或指定輸出位置
- 提供查詢介面或 Web API
