using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public class MailpitMessage
{
    public string ID { get; set; } = "";
    public string Subject { get; set; } = "";
    public string From { get; set; } = "";
    public string Created { get; set; } = "";
}

public sealed partial class EmailInboxPage : Page
{
    private static readonly HttpClient Http = new();
    private const string ApiBase = "http://127.0.0.1:8025/api/v1";

    public EmailInboxPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        await LoadEmails();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadEmails();
    }

    private async void DeleteAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Busy.IsActive = true;
            await Http.DeleteAsync($"{ApiBase}/messages");
            await LoadEmails();
        }
        catch { }
        finally { Busy.IsActive = false; }
    }

    private async Task LoadEmails()
    {
        try
        {
            Busy.IsActive = true;
            EmailList.ItemsSource = null;
            EmptyMsg.Visibility = Visibility.Collapsed;
            
            var json = await Http.GetStringAsync($"{ApiBase}/messages");
            using var doc = JsonDocument.Parse(json);
            var msgs = new List<MailpitMessage>();
            
            if (doc.RootElement.TryGetProperty("messages", out var msgArr))
            {
                foreach (var m in msgArr.EnumerateArray())
                {
                    var id = m.GetProperty("ID").GetString() ?? "";
                    var subject = m.GetProperty("Subject").GetString() ?? "(No Subject)";
                    var created = m.GetProperty("Created").GetString() ?? "";
                    var from = m.TryGetProperty("From", out var f) && f.TryGetProperty("Address", out var a) ? a.GetString() ?? "" : "";
                    
                    if (DateTime.TryParse(created, out var dt)) created = dt.ToLocalTime().ToString("g");

                    msgs.Add(new MailpitMessage { ID = id, Subject = subject, From = from, Created = created });
                }
            }
            
            if (msgs.Count == 0) EmptyMsg.Visibility = Visibility.Visible;
            else EmailList.ItemsSource = msgs;
        }
        catch
        {
            EmptyMsg.Text = "Mailpit is not running. Start it from Services.";
            EmptyMsg.Visibility = Visibility.Visible;
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    private async void EmailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmailList.SelectedItem is MailpitMessage msg)
        {
            try
            {
                ViewSubject.Text = msg.Subject;
                ViewFrom.Text = $"From: {msg.From}";
                ViewDate.Text = msg.Created;
                
                var json = await Http.GetStringAsync($"{ApiBase}/message/{msg.ID}");
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("HTML", out var htmlEl))
                {
                    var html = htmlEl.GetString();
                    if (!string.IsNullOrEmpty(html))
                    {
                        await EmailWebView.EnsureCoreWebView2Async();
                        EmailWebView.NavigateToString(html);
                        return;
                    }
                }
                
                if (doc.RootElement.TryGetProperty("Text", out var textEl))
                {
                    var text = textEl.GetString() ?? "";
                    await EmailWebView.EnsureCoreWebView2Async();
                    EmailWebView.NavigateToString($"<pre style=\"font-family: sans-serif; white-space: pre-wrap;\">{System.Net.WebUtility.HtmlEncode(text)}</pre>");
                }
            }
            catch { }
        }
    }
}
