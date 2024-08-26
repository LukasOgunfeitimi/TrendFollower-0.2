using cAlgo.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace cAlgo.Robots
{
    public class Webhook {
        private readonly bool Activated;
        private readonly HttpClient _httpClient;
        private const string _webhookUrl = "https://discord.com/api/webhooks/1259628968508788908/Fu60NYaw7JMIeKYpSn0Q-kFVkPQyrE5vFlUjt0Ozt2aAetvAiofBfZaeRbxzHTU9kau9";
        public Webhook(bool Activated) {
            this.Activated = Activated;
            _httpClient = new HttpClient();
        }
        public void SendWebhookMessage(double Volume, TradeType direction, string comment, string message) {
            SendWebhookMessage(direction + " " + message + " Volume " + Volume + " Info " + comment);
        }
        public async void SendWebhookMessage(string message) {
            if (!Activated) return;

            string jsonPayload = "{\"username\":\"TrendFollower\",\"avatar_url\":\"\",\"content\":\"<@109213512366071808> " + message + "\"}";
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(_webhookUrl, content);
        }
    }
}
