using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using PlayEveryWare.EpicOnlineServices.Samples;
using NUnit.Framework;
using System.Collections.Generic;
using Epic.OnlineServices;
using Unity.VisualScripting;
using Unity.PlasticSCM.Editor.WebApi;
using System.Linq;
using static UnityEngine.GraphicsBuffer;
using UnityEngine.Rendering;
using UnityEditor;

public class LobbyMemberData
{
    public string userName;
    public ProductUserId puid;
    public LobbyMemberDataDisplay displayUI;

    public LobbyMemberData(ProductUserId _puid, LobbyMemberDataDisplay _displayUI)
    {
        puid = _puid;
        displayUI = _displayUI;
    }
}

public class JoinedLobbyUI : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI path;
    [SerializeField] TextMeshProUGUI id;

    [SerializeField] LobbyMemberDataDisplay lobbyMemberDisplayPrefab;

    [SerializeField] Button leaveButton;

    [SerializeField] Transform memberRoot;
    [SerializeField] Transform logRoot;
    [SerializeField] GameObject logPrefab;

    List<LobbyMemberData> currentMemberDatas = new();

    public void Activated(string lobbyPath, string lobbyId, List<LobbyMember> members)
    {
        gameObject.SetActive(true);
        path.text = lobbyPath == "" ? "undefined" : lobbyPath;
        id.text = lobbyId;

        foreach(LobbyMember member in members)
        {
            AddMemberData(member);
        }
    }

    public void Deactivated()
    {
        gameObject.SetActive(false);
        ClearCurrentMemberDatas();
        ClearLog();
    }

    void AddMemberData(LobbyMember member)
    {
        var a = currentMemberDatas.Find(m=>m.puid == member.ProductId);
        if (a != null) return;

        LobbyMemberDataDisplay memberDisplay = Instantiate(lobbyMemberDisplayPrefab, memberRoot);
        memberDisplay.SetInfo(member.ProductId.ToString());
        memberDisplay.SetUserName(member.DisplayName);
        currentMemberDatas.Add(new LobbyMemberData(member.ProductId, memberDisplay));


        // 1) Layoutの計算を即時反映
        LayoutRebuilder.ForceRebuildLayoutImmediate(memberRoot.GetComponent<RectTransform>());
        // 2) 念のためキャンバス全体の更新も確定
        Canvas.ForceUpdateCanvases();
    }

    void ClearCurrentMemberDatas()
    {
        foreach(var data in currentMemberDatas)
        {
            Destroy(data.displayUI.gameObject);
        }

        currentMemberDatas.Clear();
    }

    void ClearLog()
    {
        for (int i = logs.Count - 1; i >= 0; i--)
        {
            Destroy(logs[i].gameObject);
        }

        logs.Clear();
    }

    public void OnJoined(LobbyMember member)
    {
        Debug.Log("参加");
        AddMemberData(member);
        CreateLog(member.ProductId, member.DisplayName, LobbyLogType.JOIN);
    }

    public void OnUserNameApplied(ProductUserId puid, string userName)
    {
        if (userName =="") userName = LobbySceneManager.emptyPlayerName;
     
        if (currentMemberDatas.Count <= 0) return;

        var member = currentMemberDatas.FirstOrDefault(m => m.displayUI.puid.text == puid.ToString());
        if (member == null) return;
        if (member.displayUI == null) return;

        member.displayUI.SetUserName(userName);
        member.userName = userName;

        var userLogs = logs.Where(m => m.id == puid);

        foreach(var log in userLogs)
        {
            log.UpdateNameText(userName);
        }
    }

    public void OnLeft(LobbyMember member)
    {
        Debug.Log("退室");
        var remover = currentMemberDatas.Find(m => m.puid == member.ProductId);

        string userName = remover.userName;
        if (userName == "") userName = LobbySceneManager.emptyPlayerName;
        CreateLog(member.ProductId, userName, LobbyLogType.LEAVE);

        Destroy(remover.displayUI.gameObject);
        currentMemberDatas.Remove(remover);
    }

    public void OnDead(LobbyMember member)
    {
        var taget = currentMemberDatas.FirstOrDefault(m => m.puid == member.ProductId);
        if (taget == null) return; ;
        taget.displayUI.SetDisconnect(true);
        CreateLog(member.ProductId, member.DisplayName, LobbyLogType.DISCONNECT);
    }

    public void OnRevive(LobbyMember member)
    {
        var taget = currentMemberDatas.Find(m => m.puid == member.ProductId);
        if (taget == null) return; ;
        taget.displayUI.SetDisconnect(false);
        CreateLog(member.ProductId, member.DisplayName, LobbyLogType.REVIVE);
    }

    public void OnOwnerChanged(LobbyMember newOwner)
    {
        if (currentMemberDatas.Count <= 0) return;
        
        CreateLog(newOwner.ProductId, newOwner.DisplayName, LobbyLogType.OWNER_CHANGED);

        foreach (LobbyMemberData memberData in currentMemberDatas)
        {
            bool isOwner = memberData.puid == newOwner.ProductId;
            memberData.displayUI.SetOwner(isOwner);
            if(isOwner)memberData.displayUI.transform.SetAsFirstSibling();
        }

    }

    public void HeartBeat(LobbyMember member)
    {
        var target = currentMemberDatas.FirstOrDefault(m => m.puid == member.ProductId);
        if (target == null) return;
        target.displayUI.HeartBeat();
    }

    List<LobbyActionLog> logs = new();

    void CreateLog(ProductUserId id, string userName, LobbyLogType logType)
    {
        var _log = Instantiate(logPrefab, logRoot);
        var log = _log.GetComponent<LobbyActionLog>();
        log.UpdateData(id, userName, logType);
        logs.Add(log);
    }
}
