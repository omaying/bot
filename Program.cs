using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

class Program
{
    private static readonly HttpClient client = new HttpClient();
    private const string baseUrl = "https://fapi.binance.com";

    private static readonly Dictionary<string, DateTime> signaledSymbols = new Dictionary<string, DateTime>();

    static async Task Main(string[] args)
    {
        int rsiPeriod = 6;
        int limit = 100;

        var symbols = await GetAllActiveUsdsFuturesSymbols();
        Console.WriteLine($"Toplam {symbols.Count} aktif USDS-M vadeli işlem paritesi bulundu.");
        await Task.Delay(TimeSpan.FromSeconds(2));

        while (true)
        {
            foreach (var symbol in symbols)
            {
                try
                {
                    if (signaledSymbols.ContainsKey(symbol))
                    {
                        var lastSignalTime = signaledSymbols[symbol];
                        if (DateTime.Now - lastSignalTime < TimeSpan.FromHours(1))
                        {
                            Console.WriteLine($"{symbol} için son 4 saat içinde sinyal verildi. Atlanıyor...");
                            continue;
                        }
                    }

                    var closes = await GetClosingPrices(symbol, limit, "15m");

                    if (closes.Count < rsiPeriod)
                    {
                        Console.WriteLine($"{symbol} için yeterli veri yok.");
                        continue;
                    }

                    var rsi = CalculateRSI(closes, rsiPeriod);
                    Console.WriteLine($"{symbol} RSI(6): {rsi}");

                    var currentPrice = closes.Last();

                    // Short signal: RSI > 90
                    if (rsi > 95)
                    {
                        var targetPrices = new Dictionary<string, decimal>
                        {
                            { "1. Hedef", currentPrice * 0.99m },
                            { "2. Hedef", currentPrice * 0.98m },
                            { "3. Hedef", currentPrice * 0.97m },
                            { "4. Hedef", currentPrice * 0.96m },
                            { "5. Hedef", currentPrice * 0.95m }
                        };

                        var stopLossPrice = currentPrice * 1.10m;

                        Console.WriteLine($"{symbol} için Sat Sinyali!");

                        var targetPriceMessage = string.Join("\n", targetPrices.Select(t => $"{t.Key}: {t.Value}"));

                        await SendTelegramMessage(
                            $"🚨 {symbol} SHORT Sinyali! \n\n" +
                            $"💰 Giriş Fiyatı: {currentPrice}\n\n" +
                            $"🎯 Hedef Fiyatlar:\n{targetPriceMessage}\n\n" +
                            $"⛔ Stop-Loss: {stopLossPrice}\n\n" +
                            $"⚠️ Dikkat: Fiyat düşüş beklentisi!"
                        );

                        signaledSymbols[symbol] = DateTime.Now;
                    }
                    // Long signal: RSI < 10
                    else if (rsi < 5)
                    {
                        var targetPrices = new Dictionary<string, decimal>
                        {
                            { "1. Hedef", currentPrice * 1.01m },
                            { "2. Hedef", currentPrice * 1.02m },
                            { "3. Hedef", currentPrice * 1.03m },
                            { "4. Hedef", currentPrice * 1.04m },
                            { "5. Hedef", currentPrice * 1.05m }
                        };

                        var stopLossPrice = currentPrice * 0.90m;

                        Console.WriteLine($"{symbol} için Al Sinyali!");

                        var targetPriceMessage = string.Join("\n", targetPrices.Select(t => $"{t.Key}: {t.Value}"));

                        await SendTelegramMessage(
                            $"🚨 {symbol} LONG Sinyali! \n\n" +
                            $"💰 Giriş Fiyatı: {currentPrice}\n\n" +
                            $"🎯 Hedef Fiyatlar:\n{targetPriceMessage}\n\n" +
                            $"⛔ Stop-Loss: {stopLossPrice}\n\n" +
                            $"⚠️ Dikkat: Fiyat artış beklentisi!"
                        );

                        signaledSymbols[symbol] = DateTime.Now;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{symbol} işlenirken hata oluştu: {ex.Message}");
                }
            }

          
        }
    }

    private static async Task<List<string>> GetAllActiveUsdsFuturesSymbols()
    {
        var url = $"{baseUrl}/fapi/v1/exchangeInfo";
        var response = await client.GetStringAsync(url);
        var exchangeInfo = JObject.Parse(response);
        var symbols = exchangeInfo["symbols"]
            .Where(s => s["contractType"].ToString() == "PERPETUAL" &&
                        s["status"].ToString() == "TRADING")
            .Select(s => s["symbol"].ToString())
            .ToList();

        return symbols;
    }

    private static async Task<List<decimal>> GetClosingPrices(string symbol, int limit, string interval)
    {
        var url = $"{baseUrl}/fapi/v1/klines?symbol={symbol}&interval={interval}&limit={limit}";
        var response = await client.GetStringAsync(url);
        var candles = JArray.Parse(response);

        return candles.Select(c => c[4].ToObject<decimal>()).ToList();
    }

    private static decimal CalculateRSI(List<decimal> prices, int period)
    {
        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < prices.Count; i++)
        {
            var difference = prices[i] - prices[i - 1];
            if (difference > 0)
            {
                gains.Add(difference);
                losses.Add(0);
            }
            else
            {
                losses.Add(Math.Abs(difference));
                gains.Add(0);
            }
        }

        var avgGain = gains.Take(period).Average();
        var avgLoss = losses.Take(period).Average();

        for (int i = period; i < gains.Count; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
        }

        if (avgLoss == 0)
        {
            return 100;
        }

        var rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    private static async Task SendTelegramMessage(string message)
    {
        var url = $"https://api.telegram.org/bot{telegramBotToken}/sendMessage";
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("chat_id", telegramChatId),
            new KeyValuePair<string, string>("text", message)
        });

        var response = await client.PostAsync(url, content);
        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine("Telegram mesajı başarıyla gönderildi.");
        }
        else
        {
            Console.WriteLine("Telegram mesajı gönderilirken hata oluştu.");
        }
    }
}
