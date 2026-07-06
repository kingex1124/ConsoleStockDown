using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConsoleStockDown.Models;

/// <summary>
/// 對應單一來源整理後法人資料的內部載體。
/// </summary>
internal sealed record InstitutionalTradePayload(
    string TradeDate,
    string RawTradeDate,
    List<List<JsonElement>> Records);

/// <summary>
/// 對應 TWSE <c>T86</c> API 最外層回傳結構的內部模型。
/// </summary>
internal sealed record TwseInstitutionalTradeApiResponse
{
    /// <summary>
    /// API 回傳狀態。
    /// </summary>
    [JsonPropertyName("stat")]
    public string Stat { get; init; } = string.Empty;

    /// <summary>
    /// API 回傳的交易日期。
    /// </summary>
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    /// <summary>
    /// API 回傳的明細資料列。
    /// </summary>
    [JsonPropertyName("data")]
    public List<List<JsonElement>> Data { get; init; } = [];
}

/// <summary>
/// 對應 TPEX 三大法人 API 最外層回傳結構的內部模型。
/// </summary>
internal sealed record OtcInstitutionalTradeApiResponse
{
    /// <summary>
    /// API 回傳狀態。
    /// </summary>
    [JsonPropertyName("stat")]
    public string Stat { get; init; } = string.Empty;

    /// <summary>
    /// API 回傳的交易日期。
    /// </summary>
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    /// <summary>
    /// API 回傳的資料表集合。
    /// </summary>
    [JsonPropertyName("tables")]
    public List<OtcInstitutionalTradeApiTable> Tables { get; init; } = [];
}

/// <summary>
/// 對應 TPEX 三大法人 API 單一資料表的內部模型。
/// </summary>
internal sealed record OtcInstitutionalTradeApiTable
{
    /// <summary>
    /// 資料表對應的原始日期字串。
    /// </summary>
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    /// <summary>
    /// 資料表明細資料列。
    /// </summary>
    [JsonPropertyName("data")]
    public List<List<JsonElement>> Data { get; init; } = [];
}
