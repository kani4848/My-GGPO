using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using PlayEveryWare.EpicOnlineServices.Samples;
using NUnit.Framework;
using System.Collections.Generic;
using Epic.OnlineServices;

public class LobbyMemberData
{
    public string name;
    public string puid;
    public bool isOwneer;

    public LobbyMemberData(string _name, string _puid, bool _isOwner)
    {
        name = _name;
        puid = _puid;
        isOwneer = _isOwner;
    }
}

public class JoinedLobbyUI : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI path;
    [SerializeField] TextMeshProUGUI id;

    [SerializeField] LobbyMemberDataDisplay lobbyMemberDisplayPrefab;
    [SerializeField] LobbyMemberDataDisplay ownerData;

    [SerializeField] Button leaveButton;

    [SerializeField] Transform memberRoot;
    [SerializeField] Transform logRoot;
    [SerializeField] TextMeshPro logText;

    public void Activated(string lobbyPath, string lobbyId, List<LobbyMemberData> memberDatas)
    {
        gameObject.SetActive(true);
        path.text = lobbyPath == "" ? "undefined" : lobbyPath;
        id.text = lobbyId;

        RefreshMemberDisplay(memberDatas);
    }

    public void Deactivated()
    {
        gameObject.SetActive(false);
    }

    void RefreshMemberDisplay(List<LobbyMemberData> memberDatas)
    {
        foreach(LobbyMemberData member in memberDatas)
        {
            if(member.isOwneer)
            {
                ownerData.SetInfo(member.name, member.puid);
            }
            else
            {
                LobbyMemberDataDisplay memberDisplay = Instantiate(lobbyMemberDisplayPrefab, memberRoot);
                memberDisplay.SetInfo(member.name, member.puid);
            }
        }
    }

    const string joinedMessage = "is joined.";
    const string leftMessage = "is left.";

    public void OnJoined(ProductUserId puid, string name)
    {
        Debug.Log(name + joinedMessage);
        var log = Instantiate(logText, logRoot);
        log.text = $"{name} enter the lobby.";
    }


    public void OnLeft(ProductUserId puid, string name)
    {
        Debug.Log(name + leftMessage);
        var log = Instantiate(logText, logRoot);
        log.text = $"{name} leave the lobby.";
    }
}
