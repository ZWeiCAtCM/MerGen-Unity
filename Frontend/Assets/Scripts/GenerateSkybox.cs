using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;  // 需要导入 Newtonsoft.Json 处理 JSON
using UnityEngine.Networking;
using PimDeWitte.UnityMainThreadDispatcher;  // ✅ 使用 PimDeWitte 版本的 UnityMainThreadDispatcher
using TMPro;  // 🔹 如果你使用的是 TextMeshPro 需要导入这个

public class GenerateSkybox : MonoBehaviour
{
    public Button generateButton;  // 生成按钮
    public TextMeshProUGUI buttonText;  // 🔹 确保这是 TextMeshProUGUI
    public TextMeshProUGUI llamaReplyText; // llama 的回复内容作为 prompt
    private ClientWebSocket webSocket;
    private bool isGenerating = false;  // 是否正在生成
    private string apiUrl = "http://localhost:8000/api/pano-gen/generate_with_image/";

    private async void Start()
    {
        if (generateButton == null)
            generateButton = GetComponent<Button>();

        if (buttonText == null)
            buttonText = generateButton.GetComponentInChildren<TextMeshProUGUI>();  // 🔹 自动查找 TMP 组件

        if (generateButton != null)
            generateButton.onClick.AddListener(OnGenerateButtonClick);

        await ConnectWebSocket();
    }

    public void OnGenerateButtonClick()
    {
        if (isGenerating) return; // 防止重复点击

        StartCoroutine(GenerateSkyboxCoroutine());
    }

    private IEnumerator GenerateSkyboxCoroutine()
    {
        isGenerating = true;
        generateButton.interactable = false;

        if (buttonText != null)
            buttonText.text = "Generating...";

        Debug.Log("⏳ Sending API request to generate skybox...");

        string rawPrompt = llamaReplyText != null ? llamaReplyText.text : "Default prompt";
        string prompt = TruncateToLastCompleteSentence(rawPrompt);

        Dictionary<string, string> requestBody = new Dictionary<string, string>
        {
            { "prompt", prompt }
        };

        string jsonBody = JsonConvert.SerializeObject(requestBody);
        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"✅ API Response: {request.downloadHandler.text}");
            }
            else
            {
                Debug.LogError($"❌ API Call Failed: {request.error}");
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    generateButton.interactable = true;
                    if (buttonText != null)
                        buttonText.text = "Generate";
                    isGenerating = false;
                });
            }
        }

        Debug.Log("✅ Skybox request sent! Waiting for WebSocket update...");
    }

    private string TruncateToLastCompleteSentence(string input, int maxLength = 500)
    {
        if (input.Length <= maxLength)
            return input;

        string truncated = input.Substring(0, maxLength);

        int lastPunctuation = Mathf.Max(
            truncated.LastIndexOf('.'),
            Mathf.Max(truncated.LastIndexOf('!'), truncated.LastIndexOf('?'))
        );

        if (lastPunctuation > 0)
        {
            return truncated.Substring(0, lastPunctuation + 1); // 包含标点
        }
        else
        {
            return truncated; // 如果没有标点，就直接裁切
        }
    }

    private async Task ConnectWebSocket()
    {
        string serverUri = "ws://localhost:8000/ws/skybox-updates/";
        webSocket = new ClientWebSocket();

        try
        {
            await webSocket.ConnectAsync(new System.Uri(serverUri), CancellationToken.None);
            Debug.Log("✅ WebSocket connected!");

            await ListenForMessages();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ WebSocket connection failed: {e.Message}");
        }
    }

    private async Task ListenForMessages()
    {
        byte[] buffer = new byte[1024];

        while (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(new System.ArraySegment<byte>(buffer), CancellationToken.None);
            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);

            Debug.Log($"🔹 WebSocket received: {message}");

            if (message.Contains("Skybox updated"))
            {
                EnableButton();
            }
        }
    }

    private void EnableButton()
    {
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            if (generateButton != null)
                generateButton.interactable = true;

            if (buttonText != null)
                buttonText.text = "Generate";

            isGenerating = false;
        });

        Debug.Log("✅ Skybox updated! Button enabled.");
    }

    private void OnDestroy()
    {
        if (webSocket != null)
        {
            webSocket.Dispose();
            webSocket = null;
        }
    }
}