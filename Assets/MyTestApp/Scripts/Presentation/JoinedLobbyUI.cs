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
    [SerializeField] TextMeshProUGUI logText;

    List<LobbyMemberData> currentMemberDatas = new();

    public void Activated(string lobbyPath, string lobbyId, List<LobbyMember> members)
    {
        gameObject.SetActive(true);
        path.text = lobbyPath == "" ? "undefined" : lobbyPath;
        id.text = lobbyId;

        Debug.Log("ロビー起動");

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
        for (int i = logRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(logRoot.GetChild(i).gameObject);
        }
    }

    public void OnJoined(LobbyMember member)
    {
        Debug.Log("参加");
        AddMemberData(member);
    }

    public void OnUserNameApplied(ProductUserId puid, string userName)
    {
        if (userName =="")userName = LobbySceneManager.emptyPlayerName;
        var log = Instantiate(logText, logRoot);
        log.text = $"{userName} enter the lobby.";

        if (currentMemberDatas.Count <= 0) return;

        var member = currentMemberDatas.First(m => m.displayUI.puid.text == puid.ToString());
        member.displayUI.SetUserName(userName);
        member.userName = userName;
    }

    public void OnLeft(LobbyMember member)
    {
        Debug.Log("退室");
        var remover = currentMemberDatas.Find(m => m.puid == member.ProductId);

        string userName = remover.userName;
        if (userName == "") userName = LobbySceneManager.emptyPlayerName;
        var log = Instantiate(logText, logRoot);
        log.text = $"{userName} leave the lobby.";

        Destroy(remover.displayUI.gameObject);
        currentMemberDatas.Remove(remover);
    }

    public void OnDead(LobbyMember member)
    {
        var taget = currentMemberDatas.Find(m => m.puid == member.ProductId);
        taget.displayUI.SetDisconnect(true);
    }

    public void OnOwnerChanged(LobbyMember newOwner)
    {
        if (currentMemberDatas.Count <= 0) return;

        foreach (LobbyMemberData memberData in currentMemberDatas)
        {
            bool isOwner = memberData.puid == newOwner.ProductId;
            memberData.displayUI.SetOwner(isOwner);
            if(isOwner)memberData.displayUI.transform.SetAsFirstSibling();
        }

    }
}
