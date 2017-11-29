﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System;
using System.Text;

namespace MultipleBattle
{
	//TODO 保存されるメセージが必要だそうです。
	//TODO 如果客户端后发的信息先到怎么处理？
	//（1，简单的做法把客户端先发后到的信息忽略。）
	//（2，复杂的做法，记录收到的客户端发送的帧，这就需要客户端也定时向服务器端发送帧信息，这样服务器端的压力会增加。）
	//TODO 中途加入，验证用户，然后把存放的帧重新发给客户端。（需要重新验证用户和储存帧。）
	//TODO 用这种方式可以把AI独立出来，也就是说随时可以写有限的ai逻辑彼此对战（看谁的AI更好），但是游戏本身的逻辑不会改变。
	public class BattleServer : NetworkManager
	{

		public bool isBattleBegin;
		//送信したフレーム号。
		int mFrame = 0;
		float mNextFrameTime;
		Dictionary<int,HandleMessage> mHandleMessages;
		Dictionary<int,PlayerStatus> mConnections;
		float mFrameInterval;

		public bool isCacheMessage;
		StringBuilder mStringBuilder;

		void Awake ()
		{
			Reset ();
			this.networkPort = NetConstant.listene_port;
			this.StartServer ();
			connectionConfig.SendDelay = 1;
			NetworkServer.maxDelay = 0;
			NetworkServer.RegisterHandler (MessageConstant.CLIENT_READY,OnRecieveClientReady);
			NetworkServer.RegisterHandler (MessageConstant.CLIENT_PLAYER_HANDLE,OnRecievePlayerHandle);
			NetworkServer.RegisterHandler (MessageConstant.CLIENT_REQUEST_FRAMES, OnRecievePlayerFrameRequest);
			NetworkServer.RegisterHandler (MsgType.Connect, OnClientConnect);
			NetworkServer.RegisterHandler (MsgType.Disconnect, OnClientDisconnect);
			mStringBuilder = new StringBuilder ();
			mCachedLines = new List<CachedLine> ();
			//Environmentでシステムのパラメーターをセートする
			string[] commandLineArgs = Environment.GetCommandLineArgs();
			for(int i=0;i<commandLineArgs.Length;i++){
				if(commandLineArgs[i].ToLower().IndexOf("playercount")!=-1){
					string[] countStrs = commandLineArgs[i].Split(new char[]{'='});
					if(countStrs.Length>1){
						int playerCount = 0;
						if(int.TryParse(countStrs[1],out playerCount)){
							if (playerCount > 0) {
								NetConstant.max_player_count = playerCount;
							}
						}
					}
				}
			}
			mFrameInterval = 1f / NetConstant.FRAME_RATE;
		}

		//サーバーをリセットーする
		public void Reset(){
			isBattleBegin = false;
			mConnections = new Dictionary<int, PlayerStatus> ();
			mHandleMessages = new Dictionary<int, HandleMessage> ();
			mFrame = 0;
			mNextFrameTime = 0;
		}

		void Update ()
		{
			if (!isBattleBegin) {
				return;
			}
			SendFrame ();
			if(this.mConnections.Count == 0){
				isBattleBegin = false;
				mFrame = 0;
			}
		}

		public int ConnectionCount{
			get{ 
				return mConnections.Count;
			}
		}

		#region 1.Send
		//当有玩家加入或者退出或者准备的場合
		//プレイヤーを入る、去るとか、準備できた時とか、メセージを送る
		void SendPlayerStatus(){
			List<PlayerStatus> pss = new List<PlayerStatus> ();
			foreach(PlayerStatus ps in mConnections.Values){
				pss.Add (ps);
			}
			Debug.Log (pss.Count);
			PlayerStatusArray psa = new PlayerStatusArray ();
			psa.playerStatus = pss.ToArray ();
			NetworkServer.SendToAll (MessageConstant.SERVER_CLIENT_STATUS,psa);
		}

		//告诉客户端创建人物
		//クライアントにキャラクターを作成する
		void SendBattleBegin(){
			CreatePlayer cp = new CreatePlayer ();
			List<int> playerIds = new List<int> ();
			foreach(NetworkConnection nc in NetworkServer.connections){
				Debug.Log (nc);
				if(nc!=null)
				playerIds.Add (nc.connectionId);
			}
			cp.playerIds = playerIds.ToArray ();
			NetworkServer.SendToAll (MessageConstant.CLIENT_READY,cp);
		}

		//メセージをクライアントに送る
		void SendFrame(){
			//这样做能保证帧率恒定不变
			while(mNextFrameTime <= Time.fixedUnscaledTime){
				mNextFrameTime += mFrameInterval;
				SendFrameMessage ();
			}
		}

		//メセージをクライアントに送る
		void SendFrameMessage(){
			ServerMessage currentMessage = new ServerMessage ();
			ConstructFrameMessageAndIncreaseFrameIndex (currentMessage);
			NetworkServer.SendUnreliableToAll (MessageConstant.SERVER_TO_CLIENT_MSG, currentMessage);
		}

		//メセージを構造して、フレーム番号が増える
		void ConstructFrameMessageAndIncreaseFrameIndex(ServerMessage currentMessage){
			currentMessage.frame = mFrame;
			List<HandleMessage> handleMessages = new List<HandleMessage> ();
			int i = 0;
			foreach(int playerId in mHandleMessages.Keys){
				handleMessages.Add (mHandleMessages [playerId]);
				mHandleMessages [playerId] = null;
				i++;
			}
			currentMessage.handleMessages = handleMessages.ToArray();
			mFrame++;
		}
		#endregion

		#region 2.Recieve
		void OnClientConnect (NetworkMessage nm)
		{
			Debug.Log ("OnClientConnect");
			NetworkConnection conn = nm.conn;
			if (isBattleBegin || mConnections.Count >= NetConstant.max_player_count) {
				conn.Disconnect ();
			}else {
				PlayerStatus ps = new PlayerStatus ();
				ps.playerId = conn.connectionId;
				ps.isReady = false;
				mConnections.Add (conn.connectionId,ps);
				SendPlayerStatus ();
				if(isCacheMessage){
					
				}
			}
		}

		void OnClientDisconnect (NetworkMessage nm)
		{
			Debug.Log ("OnClientDisconnect");
			NetworkConnection conn = nm.conn;
			mConnections.Remove(conn.connectionId);

			if (mConnections.Count == 0) {
				Reset ();
			} else {
				SendPlayerStatus ();
			}
		}

		//收到用户准备
		//ユーザーを準備できたメセージを
		void OnRecieveClientReady(NetworkMessage msg){
			Debuger.Log ("OnRecieveClientReady");
			if(mConnections.ContainsKey(msg.conn.connectionId)){
				mConnections [msg.conn.connectionId].isReady = true;
			}
			int count = 0;
			foreach(PlayerStatus ps in mConnections.Values){
				if (ps.isReady) {
					count++;
				} 
			}
			if (count >= NetConstant.max_player_count) {
				isBattleBegin = true;
				SendBattleBegin ();
				mNextFrameTime = Time.realtimeSinceStartup;
			} 
			SendPlayerStatus ();
		}

		//收到用户请求丢失的帧
		// ユーザーからフレーム
		void OnRecievePlayerFrameRequest(NetworkMessage msg){
		
		}

		//收到操作
		//プレーヤーの操作を受ける
		void OnRecievePlayerHandle(NetworkMessage msg){
			HandleMessage playerHandle = msg.ReadMessage<HandleMessage> ();
			playerHandle.playerId = msg.conn.connectionId;
			if (!mHandleMessages.ContainsKey (playerHandle.playerId)) {
				mHandleMessages.Add (playerHandle.playerId, playerHandle);
			} else {
				mHandleMessages[playerHandle.playerId] = playerHandle;
			}
		}
		#endregion


		#region 3.Debug message

		List<CachedLine> mCachedLines;
//		int mMaxCacheLine = 20;

		void AddDebugMessage(string msg){
			CachedLine newLine = new CachedLine ();
			newLine.startIndex = mStringBuilder.Length;
			newLine.count = msg.Length + 1;
			mCachedLines.Add (newLine);

//			if(mMaxCacheLine){
//				CachedLine oldLine = mCachedLines [0];
//				mCachedLines.RemoveAt (0);
//			}

			mStringBuilder.AppendLine (msg);
		}

		struct CachedLine{
			public int startIndex;
			public int count;
		}

		#endregion
	}
}
