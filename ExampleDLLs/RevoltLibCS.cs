using CLC;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

public class RevoltClient
{
    private readonly HttpClient _client = new HttpClient();
    private string _token;

    public async Task LoginAsync(string email, string password)
    {
        var body = JsonConvert.SerializeObject(new
        {
            email,
            password
        });

        var loginRes = await _client.PostAsync("https://api.revolt.chat/auth/session/login", new StringContent(body, Encoding.UTF8, "application/json"));
        var loginJson = await loginRes.Content.ReadAsStringAsync();

        dynamic result = JsonConvert.DeserializeObject(loginJson);
        _token = result.token;
        _client.DefaultRequestHeaders.Add("X-Session-Token", _token);
    }   

    public async Task SendMessageAsync(string channelId, string message)
    {
        var body = JsonConvert.SerializeObject(new
        {
            content = message
        });

        string url = $"https://api.revolt.chat/channels/{channelId}/messages";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var res = await _client.SendAsync(request);
    }
}


public static class RevoltLibCS
{
    public static Dictionary<string, Func<Scope, Variable[], Variable?>> Run(Scope scope)
    {
        RevoltClient client = new RevoltClient();
        Dictionary<string, Func<Scope, Variable[], Variable?>> extensionFunctions = new()
        {
            {"revoltclc.login", (Scope scope, Variable[] value) =>
                {
                    if(value.Length != 2)
                    {
                        CLCPublic.ThrowError("dll-revoltclc-login", "Need 2 arguments! Got "+value.Length);
                    }
                    client.LoginAsync(value[0].Value.ToString(), value[1].Value.ToString()).Wait();
                    return null;
                }
            },
            {"revoltclc.send", (Scope scope, Variable[] value) =>
                {
                    if(value.Length != 2)
                    {
                        CLCPublic.ThrowError("dll-revoltclc-login", "Need 2 arguments! Got "+value.Length);
                    }

                    client.SendMessageAsync(value[0].Value.ToString(), value[1].Value.ToString()).Wait();
                    return null;
                }
            }
        };

        return extensionFunctions;
    }
}