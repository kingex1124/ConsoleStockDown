# Database Schema

## StockDaily 資料表結構

| 欄位名稱 | API 欄位名稱 | 資料型別 | 是否可空 | 中文說明 |
| --- | --- | --- | --- | --- |
| Id | - | int | 否 | 主鍵，自動遞增識別碼 |
| TradeDate | Date / Date | string | 否 | 交易日期，格式為 `yyyy-MM-dd` |
| RawDate | Date / Date | string | 否 | 原始 API 回傳日期，可能為民國年或西元年格式 |
| StockCode | Code / SecuritiesCompanyCode | string | 否 | 股票代號 |
| StockName | Name / CompanyName | string | 否 | 股票名稱 |
| TradeVolume | TradeVolume / TradingShares | long | 否 | 成交股數 |
| TradeValue | TradeValue / TransactionAmount | long | 否 | 成交金額 |
| OpeningPrice | OpeningPrice / Open | decimal | 否 | 開盤價 |
| HighestPrice | HighestPrice / High | decimal | 否 | 最高價 |
| LowestPrice | LowestPrice / Low | decimal | 否 | 最低價 |
| ClosingPrice | ClosingPrice / Close | decimal | 否 | 收盤價 |
| PriceChange | Change / Change | decimal | 否 | 漲跌價差 |
| TransactionCount | Transaction / TransactionNumber | int | 否 | 成交筆數 |
| ChangeRate | - | decimal? | 是 | 漲跌幅（相對前一交易日收盤價的變化率） |

## InstitutionalTradeDaily 資料表結構

| 欄位名稱 | API 欄位名稱 | 資料型別 | 是否可空 | 中文說明 |
| --- | --- | --- | --- | --- |
| Id | - | int | 否 | 主鍵，自動遞增識別碼 |
| TradeDate | date | string | 否 | 交易日期，格式為 `yyyy-MM-dd` |
| RawDate | date | string | 否 | 原始 API 回傳日期，格式通常為 `yyyyMMdd` |
| StockCode | 證券代號 | string | 否 | 股票代號 |
| StockName | 證券名稱 | string | 否 | 股票名稱 |
| ForeignInvestorBuy | 外陸資買進股數(不含外資自營商) | long | 否 | 外資及陸資買進股數，不含外資自營商 |
| ForeignInvestorSell | 外陸資賣出股數(不含外資自營商) | long | 否 | 外資及陸資賣出股數，不含外資自營商 |
| ForeignInvestorNet | 外陸資買賣超股數(不含外資自營商) | long | 否 | 外資及陸資買賣超股數，不含外資自營商 |
| ForeignDealerBuy | 外資自營商買進股數 | long | 否 | 外資自營商買進股數 |
| ForeignDealerSell | 外資自營商賣出股數 | long | 否 | 外資自營商賣出股數 |
| ForeignDealerNet | 外資自營商買賣超股數 | long | 否 | 外資自營商買賣超股數 |
| InvestmentTrustBuy | 投信買進股數 | long | 否 | 投信買進股數 |
| InvestmentTrustSell | 投信賣出股數 | long | 否 | 投信賣出股數 |
| InvestmentTrustNet | 投信買賣超股數 | long | 否 | 投信買賣超股數 |
| DealerNet | 自營商買賣超股數 | long | 否 | 自營商整體買賣超股數 |
| DealerSelfBuy | 自營商買進股數(自行買賣) | long | 否 | 自營商自行買賣買進股數 |
| DealerSelfSell | 自營商賣出股數(自行買賣) | long | 否 | 自營商自行買賣賣出股數 |
| DealerSelfNet | 自營商買賣超股數(自行買賣) | long | 否 | 自營商自行買賣買賣超股數 |
| DealerHedgeBuy | 自營商買進股數(避險) | long | 否 | 自營商避險買進股數 |
| DealerHedgeSell | 自營商賣出股數(避險) | long | 否 | 自營商避險賣出股數 |
| DealerHedgeNet | 自營商買賣超股數(避險) | long | 否 | 自營商避險買賣超股數 |
| InstitutionalInvestorsNet | 三大法人買賣超股數 | long | 否 | 三大法人合計買賣超股數 |

## 說明

- `Id` 為資料表主鍵，透過 Linq2DB 的 `Identity` 屬性自動遞增。
- `TradeDate` 用於查詢與判斷資料是否已存在，格式統一為 `yyyy-MM-dd`。
- `RawDate` 用於保留原始 API 回傳日期，方便除錯與比對。
- `StockDaily` 同時儲存 TWSE `STOCK_DAY_ALL` 與 TPEX `tpex_mainboard_quotes` 的日資料。
- `ChangeRate` 只有當存在前一交易日資料時才會計算，否則可為 `NULL`。
- 上櫃 API 若回傳 `除息`、`除權`、`除權息`、`---` 或 `----` 等非數值內容，系統會以 `0` 寫入對應數值欄位以避免解析失敗。
- `InstitutionalTradeDaily` 來自 TWSE `T86` API 與 TPEX `3itrade_hedge_result` API，若未指定抓取日期，`TradeDate` 會依 `ApiUrl` 本次抓回的上市交易日轉成上市 `yyyyMMdd` 與上櫃民國 `yyy/MM/dd` 後查詢，確保兩張表使用相同交易日。
- 寫入 `InstitutionalTradeDaily` 前，系統會先以同交易日 `StockDaily` 已存在的股票代碼過濾上市與上櫃法人資料，只保留本專案實際需要的股票。
- 上市與上櫃法人資料會先合併後再寫入同一張 `InstitutionalTradeDaily` 資料表。
- 資料庫建置時會建立 `StockDaily` 與 `InstitutionalTradeDaily` 資料表，並將對應 API 回傳資料寫入各自的資料表。
