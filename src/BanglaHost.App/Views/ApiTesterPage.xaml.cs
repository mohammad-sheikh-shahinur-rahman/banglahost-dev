using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace BanglaHost.App.Views
{

public sealed partial class ApiTesterPage : Page
{
    // Shared client so connection pooling + DNS caching work across requests.
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,   // local mkcert / self-signed
    })
    { Timeout = TimeSpan.FromSeconds(120) };

    private CancellationTokenSource? _cts;
    private string _lastRespText = "";

    public ApiTesterPage() => InitializeComponent();

    private void Auth_Changed(object s, SelectionChangedEventArgs e)
    {
        if (AuthField1 == null || AuthField2 == null) return;
        var kind = (AuthBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
        if (kind == "Bearer token")
        {
            AuthField1.PlaceholderText = "token";
            AuthField1.Visibility = Visibility.Visible;
            AuthField2.Visibility = Visibility.Collapsed;
        }
        else if (kind == "Basic (user + pass)")
        {
            AuthField1.PlaceholderText = "user";
            AuthField2.PlaceholderText = "password";
            AuthField1.Visibility = Visibility.Visible;
            AuthField2.Visibility = Visibility.Visible;
        }
        else
        {
            AuthField1.Visibility = Visibility.Collapsed;
            AuthField2.Visibility = Visibility.Collapsed;
        }
    }

    private void BodyType_Changed(object s, SelectionChangedEventArgs e)
    {
        if (BodyBox == null) return;
        var kind = (BodyTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
        BodyBox.PlaceholderText = kind switch
        {
            "JSON"             => "{ \"name\": \"test\" }",
            "Form URL-encoded" => "key1=value1&key2=value2",
            "Raw text"         => "any text body",
            _                  => "(no body)",
        };
    }

    private void FormatJson_Click(object s, RoutedEventArgs e)
    {
        var t = BodyBox.Text?.Trim() ?? "";
        if (t.Length == 0) return;
        try
        {
            using var doc = JsonDocument.Parse(t);
            BodyBox.Text = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { StatusText.Text = "JSON parse: " + ex.Message; }
    }

    private void CopyResp_Click(object s, RoutedEventArgs e)
    {
        if (_lastRespText.Length == 0) return;
        var dp = new DataPackage();
        dp.SetText(_lastRespText);
        Clipboard.SetContent(dp);
        StatusText.Text = "copied";
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => _cts?.Cancel();

    private async void Send_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var url = (UrlBox.Text ?? "").Trim();
            if (url.Length == 0) { StatusText.Text = "enter a URL"; return; }
            if (!url.Contains("://")) url = "http://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) { StatusText.Text = "invalid URL"; return; }

            var method = (MethodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancelBtn.IsEnabled = true;
            Busy.IsActive = true;
            StatusText.Text = "sending…";
            TimingText.Text = ""; SizeText.Text = "";
            RespBody.Text = ""; RespHeaders.Text = "";

        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), uri);

            foreach (var line in (HeadersBox.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var i = line.IndexOf(':');
                if (i <= 0) continue;
                var k = line[..i].Trim();
                var v = line[(i + 1)..].Trim();
                if (k.Length == 0) continue;
                if (!req.Headers.TryAddWithoutValidation(k, v))
                    (req.Content ??= new StringContent("")).Headers.TryAddWithoutValidation(k, v);
            }

            var authKind = (AuthBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
            if (authKind == "Bearer token" && AuthField1.Text.Length > 0)
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + AuthField1.Text);
            else if (authKind == "Basic (user + pass)")
            {
                var b = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{AuthField1.Text}:{AuthField2.Text}"));
                req.Headers.TryAddWithoutValidation("Authorization", "Basic " + b);
            }

            var bodyKind = (BodyTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
            var body = BodyBox.Text ?? "";
            if (bodyKind != "None" && body.Length > 0 && method != "GET" && method != "HEAD")
            {
                req.Content = bodyKind switch
                {
                    "JSON"             => new StringContent(body, Encoding.UTF8, "application/json"),
                    "Form URL-encoded" => new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
                    _                  => new StringContent(body, Encoding.UTF8, "text/plain"),
                };
            }

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
            var bytes = await resp.Content.ReadAsByteArrayAsync(_cts.Token);
            sw.Stop();

            var text = Encoding.UTF8.GetString(bytes);
            var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (ct.Contains("json", StringComparison.OrdinalIgnoreCase) && text.Length > 0 && text.Length < 2_000_000)
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    text = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                }
                catch { }
            }
            _lastRespText = text;
            RespBody.Text = text.Length > 500_000 ? text[..500_000] + "\n\n… (truncated at 500 KB)" : text;

            var sb = new StringBuilder();
            foreach (var h in resp.Headers)  sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
            foreach (var h in resp.Content.Headers) sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
            RespHeaders.Text = sb.ToString();

            StatusText.Text = $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
            TimingText.Text = $"· {sw.ElapsedMilliseconds} ms";
            SizeText.Text   = $"· {FormatBytes(bytes.LongLength)}";
        }
            catch (TaskCanceledException) { StatusText.Text = _cts?.IsCancellationRequested == true ? "cancelled" : "timed out"; }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex)          { StatusText.Text = "error"; RespBody.Text = ex.Message; }
            finally
            {
                Busy.IsActive = false;
                CancelBtn.IsEnabled = false;
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            StatusText.Text = "error";
            RespBody.Text = ex.Message;
        }
    }

    private static string FormatBytes(long n) =>
        n < 1024 ? $"{n} B" : n < 1024 * 1024 ? $"{n / 1024.0:0.#} KB" : $"{n / 1048576.0:0.##} MB";

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch { }
        finally
        {
            _cts = null;
        }
    }
}

}
