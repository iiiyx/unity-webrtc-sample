using NativeWebSocket;
using System;
using UnityEngine;

public class WebsocketClient
{
  WebSocket websocket;
  public delegate void WebSocketMessageEventHandler(string message);

  // Start is called before the first frame update
  public WebsocketClient(WebSocketMessageEventHandler onMessage)
  {
    websocket = new WebSocket("ws://localhost:5442/ws-test");

    websocket.OnOpen += () =>
    {
      Debug.Log("Connection open!");
    };

    websocket.OnError += (e) =>
    {
      Debug.Log("Error! " + e);
    };

    websocket.OnClose += (e) =>
    {
      Debug.Log("Connection closed!");
    };

    websocket.OnMessage += (bytes) =>
    {
      // Reading a plain text message
      var message = System.Text.Encoding.UTF8.GetString(bytes);
      Debug.Log("OnMessage! " + message);
      onMessage.Invoke(message);
    };

    // Keep sending messages at every 0.3s
    // InvokeRepeating("SendWebSocketMessage", 0.0f, 0.3f);

  }

  public async void Connect()
  {
    await websocket.Connect();
  }

  void Update()
  {
#if !UNITY_WEBGL || UNITY_EDITOR
    websocket.DispatchMessageQueue();
#endif
  }

  public async void Send(String msg)
  {
    if (websocket.State == WebSocketState.Open)
    {
      // Sending bytes
      // await websocket.Send(new byte[] { 10, 20, 30 });

      // Sending plain text
      await websocket.SendText(msg);
    }
  }

  private async void OnApplicationQuit()
  {
    await websocket.Close();
  }
}