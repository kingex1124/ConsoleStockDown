# ConsoleStockDown

`ConsoleStockDown` 是一個以 .NET 開發的主控台程式，會從台灣證券交易所與櫃買中心公開 API 下載每日個股資料與三大法人買賣資料，將資料寫入本機 SQLite，並計算相對前一交易日的漲跌幅，方便後續查詢、分析或擴充成排程服務。

## 功能特色

- 呼叫 TWSE OpenAPI 取得當日上市股票日資料
- 呼叫 TPEX OpenAPI 取得當日上櫃股票日資料
- 呼叫 TWSE `T86` API 取得三大法人買賣超日報
- 呼叫 TPEX 三大法人 API 取得上櫃三大法人買賣明細
- 自動建立 SQLite `StockDaily` 資料表
- 自動建立 SQLite `InstitutionalTradeDaily` 資料表
- 將 API 日期統一轉成 `yyyy-MM-dd`
- 依前一交易日收盤價計算 `ChangeRate`
- 三大法人 API 會沿用本次 `ApiUrl` 抓回的上市交易日組出上市與上櫃查詢日期，確保資料日期一致
- 可用 `appsettings.json` 覆寫三大法人抓取日期，方便補抓指定交易日
- 三大法人資料只保留同交易日 `StockDaily` 已存在的股票代碼
- 三大法人 API 若回應異常會自動重試 3 次，並在 log 記錄 `stat` 與回應片段
- 同交易日資料會在單一資料庫交易中整批覆寫，避免執行中斷後留下半套資料
- 寫入前先刪除同交易日舊資料，避免重複
- 同時輸出 Console 與檔案日誌
- 日誌檔依日期分檔，例如 `stock-service-2026-07-01.log`

## 執行流程

程式啟動後會依序執行以下步驟：

1. 讀取 `appsettings.json`
2. 初始化 SQLite 資料表
3. 呼叫 `https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL`
4. 解析每筆上市股票資料並轉換欄位格式
5. 查詢資料庫中的前一交易日資料
6. 以「前一交易日收盤價」計算上市資料的 `ChangeRate`
7. 刪除當前交易日既有 `StockDaily` 資料後重新寫入上市資料
8. 呼叫 `https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes`
9. 解析每筆上櫃股票資料並轉換欄位格式
10. 以「前一交易日收盤價」計算上櫃資料的 `ChangeRate`
11. 將上櫃資料與同交易日既有上市資料合併後覆寫到同一張 `StockDaily`
12. 若 `InstitutionalTradeFetchDate` 有設定則使用該日期，否則以 `ApiUrl` 本次抓回的上市交易日組成上市與上櫃法人 API 的日期參數
13. 呼叫 `https://www.twse.com.tw/rwd/zh/fund/T86`
14. 呼叫 `https://www.tpex.org.tw/web/stock/3insti/daily_trade/3itrade_hedge_result.php`
15. 以同交易日 `StockDaily` 的股票代碼清單過濾上市三大法人資料後寫入 `InstitutionalTradeDaily`
16. 接續呼叫上櫃三大法人服務，並使用相同交易日期抓取 `OtcInstitutionalTradeApiUrlTemplate`
17. 以上市已寫入資料為基礎，合併同交易日 `StockDaily` 股票代碼過濾後的上櫃三大法人資料再覆寫 `InstitutionalTradeDaily`
18. 若三大法人 API 回應異常則自動重試，並在 log 記錄 `stat` 與回應摘要
19. 將執行結果寫入 Console 與日誌檔

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
   │  ├─ InstitutionalTradeApiModels.cs
   │  ├─ InstitutionalTradeDaily.cs
   │  └─ StockDaily.cs
   ├─ Repository/
   │  ├─ IInstitutionalTradeRepository.cs
   │  ├─ IStockRepository.cs
   │  ├─ InstitutionalTradeRepository.cs
   │  └─ StockRepository.cs
   ├─ Services/
   │  ├─ IInstitutionalTradeService.cs
   │  ├─ IOtcInstitutionalTradeService.cs
   │  ├─ IOtcStockService.cs
   │  ├─ IStockService.cs
   │  ├─ InstitutionalTradeService.cs
   │  ├─ LatestTradeDateContext.cs
   │  ├─ OtcInstitutionalTradeService.cs
   │  ├─ OtcStockService.cs
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
    "OtcApiUrl": "https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes",
    "InstitutionalTradeApiUrlTemplate": "https://www.twse.com.tw/rwd/zh/fund/T86?date={date}&selectType=ALL&response=json",
    "OtcInstitutionalTradeApiUrlTemplate": "https://www.tpex.org.tw/web/stock/3insti/daily_trade/3itrade_hedge_result.php?l=zh-tw&o=json&se=EW&t=D&d={date}&s=0,asc",
    // Optional: specify a date to fetch institutional trade data.
    // Leave null to use the trade date returned by AppSettings.ApiUrl in the current run.
    // Example: if STOCK_DAY_ALL returns last Friday's date on Monday, institutional trade will also use that date.
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
| `ApiUrl` | 上市股票日資料 API 位址。 |
| `OtcApiUrl` | 上櫃股票日資料 API 位址。 |
| `InstitutionalTradeApiUrlTemplate` | 上市三大法人 API URL 範本，必須包含 `{date}` 佔位符，程式會以 `ApiUrl` 本次抓回的上市交易日轉成 `yyyyMMdd` 後代入。 |
| `OtcInstitutionalTradeApiUrlTemplate` | 上櫃三大法人 API URL 範本，必須包含 `{date}` 佔位符，程式會以 `ApiUrl` 本次抓回的上市交易日轉成民國 `yyy/MM/dd` 後代入。 |
| `InstitutionalTradeFetchDate` | 可選的三大法人抓取日期。支援 `yyyy-MM-dd` 與 `yyyyMMdd`，若為 `null` 或未設定，上市與上櫃三大法人都會改抓 `ApiUrl` 本次抓回的上市交易日。 |
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
- 同時保存上市與上櫃日資料
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
- 上櫃 API 若價格或漲跌欄位出現 `除息`、`除權`、`除權息`、`---` 或 `----`，會先以 `0` 寫入避免解析失敗
- 若有設定 `InstitutionalTradeFetchDate`，上市與上櫃三大法人資料都會使用該日期查詢對應 API
- 若未設定 `InstitutionalTradeFetchDate`，三大法人資料會以 `ApiUrl` 本次抓回的上市交易日轉成上市 `yyyyMMdd` 與上櫃民國 `yyy/MM/dd` 格式後查詢對應 API
- 三大法人資料只會保留同交易日已存在於 `StockDaily` 的股票代碼，會自動排除目前流程用不到的其他證券資料
- `ChangeRate` 計算方式為：

```text
(當日收盤價 - 前一交易日收盤價) / 前一交易日收盤價 * 100
```

- 若找不到前一交易日資料，`ChangeRate` 會保留為 `NULL`

## 主要元件說明

- `Program.cs`
  應用程式入口，負責建立 Host、載入設定、註冊 DI 與 Logging，並依序執行上市、上櫃與三大法人資料同步。
- `Services/StockService.cs`
  實作上市資料抓取、欄位轉換、漲跌幅計算與資料寫入流程。
- `Services/OtcStockService.cs`
  實作上櫃資料抓取、欄位轉換、漲跌幅計算，並保留同交易日上市資料後整批覆寫的流程。
- `Services/InstitutionalTradeService.cs`
  實作上市三大法人買賣資料抓取、日期參數轉換、欄位解析與寫入，並在完成後串接上櫃三大法人服務。
- `Services/OtcInstitutionalTradeService.cs`
  實作上櫃三大法人買賣資料抓取、欄位解析、股票代碼過濾，並保留同交易日已寫入的上市法人資料後整批覆寫。
- `Services/LatestTradeDateContext.cs`
  保存本次執行由上市日線 API 解析出的交易日，供三大法人流程在未指定日期時沿用。
- `Repository/StockRepository.cs`
  封裝 SQLite 存取邏輯，包含建表、查詢與同交易日整批覆寫。
- `Repository/InstitutionalTradeRepository.cs`
  封裝 `InstitutionalTradeDaily` 的建表、刪除與新增邏輯。
- `Logging/FileLogger.cs`
  自訂檔案日誌輸出，依日期建立不同 log 檔。
- `Models/StockDaily.cs`
  定義 `StockDaily` 資料表欄位與對應模型。
- `Models/InstitutionalTradeDaily.cs`
  定義 `InstitutionalTradeDaily` 資料表欄位與對應模型。
- `Models/InstitutionalTradeApiModels.cs`
  定義法人同步流程使用的 API 回應與中繼模型。

## 注意事項

- 程式每次執行都會以 API 最新交易日資料覆寫該交易日的既有資料。
- 上市與上櫃日資料都會在保留同交易日另一個市場資料的前提下整批覆寫 `StockDaily`，避免重跑時互相覆蓋。
- 三大法人資料預設依賴 `ApiUrl` 本次抓回的上市交易日來決定上市與上櫃法人 API 的日期參數，因此執行順序固定為先抓上市、再抓上櫃、最後抓法人。
- 三大法人資料會依同交易日 `StockDaily` 的股票代碼過濾，上市法人先寫入，再由上櫃法人服務保留既有上市資料後合併覆寫同一張 `InstitutionalTradeDaily`。
- 若 `InstitutionalTradeFetchDate` 設定格式錯誤，程式會直接拋出設定錯誤，避免誤抓資料。
- 若 `T86` API 暫時回傳異常內容，程式會自動重試 3 次，並把 `stat` 與回應片段寫入 log 方便排查。
- 同一交易日的刪除與重寫會包在單一資料庫交易內，若程式中途中止，該次變更會回滾，不會留下部分股票或法人資料。
- 股票與三大法人資料目前仍採逐筆寫入，若未來資料量或執行頻率提高，可考慮改成批次寫入。
- 若 API 回傳欄位格式變動，`StockService` 與 `InstitutionalTradeService` 的解析邏輯需要同步調整。

## 後續可擴充方向

- 增加排程執行
- 補上單元測試與整合測試
- 增加 CLI 參數，例如指定日期或指定輸出位置
- 提供查詢介面或 Web API
