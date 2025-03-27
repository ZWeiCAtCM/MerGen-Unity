using UnityEngine;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class WebSocketSkyboxUpdater : MonoBehaviour
{
    public Material oldPanorama;
    public Material newPanorama;

    private ClientWebSocket webSocket;

    async void Start()
    {
        UpdateTextures();
        string serverUri = "ws://localhost:8000/ws/skybox-updates/";
        webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(new System.Uri(serverUri), CancellationToken.None);
        await ListenForMessages();
    }

    async Task ListenForMessages()
    {
        byte[] buffer = new byte[1024];
        while (webSocket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);

            if (message.Contains("Skybox updated"))
            {
                UpdateTextures();
            }
        }
    }

    void UpdateTextures()
    {
        string unityAssetsPath = Application.dataPath + "/Material";
        newPanorama.mainTexture = LoadTexture(unityAssetsPath + "/new.jpg");
        oldPanorama.mainTexture = LoadTexture(unityAssetsPath + "/old.jpg");
    }

    Texture2D LoadTexture(string path)
    {
        byte[] fileData = System.IO.File.ReadAllBytes(path);
        Texture2D texture = new Texture2D(2, 2);
        texture.LoadImage(fileData);
        return texture;
    }
}
