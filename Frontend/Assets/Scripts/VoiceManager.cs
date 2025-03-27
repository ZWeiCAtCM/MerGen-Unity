using UnityEngine;
using UnityEngine.Events;
using TMPro;
using Oculus.Voice;
using System.Reflection;
using Meta.WitAi.CallbackHandlers;
using UnityEngine.Networking;
using Meta.WitAi.TTS.Utilities;
using System.Collections;
using Meta.WitAi.Json;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;



public class VoiceManager : MonoBehaviour
{
    [Header("Wit Configuration")]
    [SerializeField] private AppVoiceExperience appVoiceExperience;
    [SerializeField] private WitResponseMatcher responseMatcher;
    [SerializeField] private TextMeshProUGUI transcriptionText;
    [SerializeField] private TextMeshProUGUI replyText;         // 模型回复
    [SerializeField] private TTSSpeaker ttsSpeaker;             // TTS 发音组件

    [Header("Voice Events")]
    [SerializeField] private UnityEvent wakeWordDetected;
    [SerializeField] private UnityEvent<string> completeTranscription;

    private bool _voiceCommandReady;
    private WitResponseNode _lastWitResponse;
    private void Awake()
    {
        if (appVoiceExperience == null)
        {
            Debug.LogError("❌ appVoiceExperience is not assigned in the Inspector!");
            return;
        }
        if (responseMatcher == null)
        {
            Debug.LogError("❌ responseMatcher is not assigned in the Inspector!");
            return;
        }

        appVoiceExperience.VoiceEvents.OnRequestCompleted.AddListener(ReactiveVoice);
        appVoiceExperience.VoiceEvents.OnPartialTranscription.AddListener(OnPartialTranscription);
        appVoiceExperience.VoiceEvents.OnFullTranscription.AddListener(OnFullTranscription);
        appVoiceExperience.VoiceEvents.OnResponse.AddListener(OnWitResponse);

        var eventField = typeof(WitResponseMatcher).GetField("onMultiValueEvent", BindingFlags.NonPublic | BindingFlags.Instance);
        if (eventField != null && eventField.GetValue(responseMatcher) is MultiValueEvent onMultiValueEvent)
        {
            onMultiValueEvent.AddListener(WakeWordDetected);
        }
    }

    private void Start() {
        appVoiceExperience.Activate();
    }
    private void OnDestroy() {
        appVoiceExperience.VoiceEvents.OnRequestCompleted.RemoveListener(ReactiveVoice);
        appVoiceExperience.VoiceEvents.OnPartialTranscription.RemoveListener(OnPartialTranscription);
        appVoiceExperience.VoiceEvents.OnFullTranscription.RemoveListener(OnFullTranscription);
        appVoiceExperience.VoiceEvents.OnResponse.RemoveListener(OnWitResponse);

        var eventField = typeof(WitResponseMatcher).GetField("onMultiValueEvent", BindingFlags.NonPublic | BindingFlags.Instance);
        if (eventField != null && eventField.GetValue(responseMatcher) is MultiValueEvent onMultiValueEvent) {
            onMultiValueEvent.RemoveListener(WakeWordDetected);
        }
    }

    private void ReactiveVoice() => appVoiceExperience.Activate();

    private void WakeWordDetected(string[] arg0) {
        _voiceCommandReady = true;
        wakeWordDetected?.Invoke();
    }

    private void OnPartialTranscription(string transcription) {
        if (!_voiceCommandReady) return;
        transcriptionText.text = transcription;
    }

    private void OnWitResponse(WitResponseNode response)
    {
        // 只有在 wake word 被触发的情况下才记录响应
        if (!_voiceCommandReady) {
            Debug.Log("OnWitResponse: _voiceCommandReady not ready, returning...");
            return;
        }
        _lastWitResponse = response;
        Debug.Log("OnWitResponse: recording _lastWitResponse: " + _lastWitResponse.ToString());
        Debug.Log("OnWitResponse: _lastWitResponse is: " + _lastWitResponse.ToString());
        if (_lastWitResponse != null)
        {
            Debug.Log("OnWitResponse: _lastWitResponse not null, deciding where to route...");
            string topIntent = _lastWitResponse["intents"]?[0]?["name"]?.Value;
            Debug.Log("OnWitResponse: topIntent is: " + topIntent);
            if (topIntent == "wake_word") {
                Debug.Log("OnWitResponse: topIntent is wake_word, returning...");
                return;
            }
            string version = _lastWitResponse["entities"]?["version:version"]?[0]?["value"]?.Value;
            Debug.Log("OnWitResponse: version is: " + version);

            bool isAnalyseScene = topIntent == "analyse_scene";
            bool hasVersion = !string.IsNullOrEmpty(version);

            if (isAnalyseScene && hasVersion)
            {
                string versionValue = version.ToLower();
                string imageFile = versionValue switch
                {
                    "new" or "current" => "new.jpg",
                    "old" or "last" => "old.jpg",
                    "original" => "input.jpg",
                    _ => "input.jpg"
                };

                string versionText = versionValue.ToUpperInvariant();
                replyText.text = $"Analysing {versionText} scene...";
                
                Debug.Log("OnWitResponse: Starting SendAnalyseWithImage - intent is analyse_scene and version is: " + version + ", imageFile: " + imageFile + ", using prompt: " + transcriptionText.text);
                StartCoroutine(SendAnalyseWithImage(transcriptionText.text, version, imageFile));
            }
            else
            {
                Debug.Log("OnWitResponse: Starting SendPromptToBackend - intent not analyse_scene or version missing.");
                StartCoroutine(SendPromptToBackend(transcriptionText.text));
            }

            _lastWitResponse = null; // 用完后清空
        }
        else
        {
            // 如果没有收到 Wit.ai 的响应，可以考虑直接发送文本请求
            Debug.Log("OnWitResponse: _lastWitResponse is null, starting SendPromptToBackend.");
            StartCoroutine(SendPromptToBackend(transcriptionText.text));
        }
        _voiceCommandReady = false;
    }

    private void OnFullTranscription(string transcription)
    {
        if (!_voiceCommandReady) return;
        transcriptionText.text = transcription;
        completeTranscription?.Invoke(transcription);

        
    }

    // 🔁 发给 llama 接口
    private IEnumerator SendPromptToBackend(string message)
    {
        string url = "http://localhost:8000/api/llama_gateway/chat/";

        // 构建 JSON 数据
        var json = JsonUtility.ToJson(new MessageWrapper { message = message });

        var request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var responseJson = request.downloadHandler.text;
            var response = JsonUtility.FromJson<LlamaReply>(responseJson);

            // ✅ 显示 + 播放回复
            replyText.text = response.reply;
            Debug.Log("SendPromptToBackend: about to read response out...replyText.text: " + replyText.text);

            // 每个块最长200个字符
            StartCoroutine(SpeakFullTextBySentence(replyText.text, 200));
            Debug.Log("SendPromptToBackend: finished to read response out!!");
        }
        else
        {
            replyText.text = "Error: " + request.error;
            Debug.LogError("❌ Failed to reach llama backend: " + request.error);
        }
    }

    private IEnumerator SendAnalyseWithImage(string message, string version, string imageFileName)
    {
        string url = "http://localhost:8000/api/llama_gateway/chat/";
        string imagePath = Application.dataPath + "/Material/" + imageFileName;

        if (!System.IO.File.Exists(imagePath))
        {
            Debug.LogError("❌ Image not found at: " + imagePath);
            replyText.text = "Image not found: " + imageFileName;
            yield break;
        }

         // 读取原始图片数据
        byte[] originalImageData = System.IO.File.ReadAllBytes(imagePath);
        // 创建一个 Texture2D，并加载图片
        Texture2D texture = new Texture2D(2, 2);
        if (!texture.LoadImage(originalImageData))
        {
            Debug.LogError("❌ Failed to load image into Texture2D");
            yield break;
        }
        // 对图片进行压缩，EncodeToJPG 的 quality 参数取值范围为 0 到 100
        byte[] compressedImageData = texture.EncodeToJPG(50);
        Debug.Log("Compressed image size: " + compressedImageData.Length + " bytes");
        message = message + "you will give response based on the content of the image attached in my current command and mark your response as the description for " + version + " version, if in our earlier conversation we have discussed about the same version, please overwrite the previous description with this new description.";
        List<IMultipartFormSection> formData = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("message", message),
            new MultipartFormFileSection("image", compressedImageData, imageFileName, "image/jpeg")
        };

        UnityWebRequest request = UnityWebRequest.Post(url, formData);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var responseJson = request.downloadHandler.text;
            var response = JsonUtility.FromJson<LlamaReply>(responseJson);

            replyText.text = response.reply;
            // 每个块最长200个字符
            StartCoroutine(SpeakFullTextBySentence(replyText.text, 200));
        }
        else
        {
            replyText.text = "Error: " + request.error;
            Debug.LogError("❌ Failed image API: " + request.error);
        }
    }

    private List<string> GetSentences(string text)
    {
        // 正则表达式捕获完整句子（包含标点）
        string pattern = @"(.*?)([\.!\?])\s+";
        MatchCollection matches = Regex.Matches(text, pattern);
        List<string> sentences = new List<string>();

        int lastEnd = 0;
        foreach (Match match in matches)
        {
            if (match.Success)
            {
                string sentence = match.Groups[1].Value.Trim() + match.Groups[2].Value;
                sentences.Add(sentence);
                lastEnd = match.Index + match.Length;
            }
        }
        // 如果有剩余部分，则添加到句子列表中
        if (lastEnd < text.Length)
        {
            string remaining = text.Substring(lastEnd).Trim();
            if (!string.IsNullOrEmpty(remaining))
            {
                sentences.Add(remaining);
            }
        }
        return sentences;
    }

    private IEnumerator SpeakFullTextBySentence(string text, int maxChunkLength)
    {
        List<string> sentences = GetSentences(text); // 用上面提供的 GetSentences 方法
        List<string> chunks = new List<string>();
        string currentChunk = "";

        foreach (string sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length <= maxChunkLength)
            {
                currentChunk += sentence;
            }
            else
            {
                if (!string.IsNullOrEmpty(currentChunk))
                {
                    chunks.Add(currentChunk.Trim());
                }
                currentChunk = sentence;
            }
        }
        if (!string.IsNullOrEmpty(currentChunk))
        {
            chunks.Add(currentChunk.Trim());
        }

        foreach (string chunk in chunks)
        {
            Debug.Log("Speaking chunk: " + chunk);
            ttsSpeaker.Speak(chunk);

            // 根据 chunk 长度估计延时，假设朗读速度为 12.5 个字符每秒
            float estimatedTime = chunk.Length / 12.5f;
            // 你可以根据需要添加一个额外的缓冲时间
            float delayBetweenChunks = estimatedTime;
            Debug.Log("Waiting for " + delayBetweenChunks + " seconds before next chunk.");
            yield return new WaitForSeconds(delayBetweenChunks);
        }
    }



    // 数据结构
    [System.Serializable]
    private class MessageWrapper
    {
        public string message;
    }

    [System.Serializable]
    private class LlamaReply
    {
        public string session_id;
        public string reply;
    }
}