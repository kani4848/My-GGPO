using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RankingSceneManager : MonoBehaviour, IRankingSceneManager
{
    IEosService eosService;

    [SerializeField] Button globalBtn;
    [SerializeField] Button myRankBtn;
    [SerializeField] Button goLobbyBtn;

    [SerializeField] GameObject LoadingUI;

    [SerializeField] RankingNamePlate namePlatePrefab;
    [SerializeField] GameObject namePlatesRoot;
    List<RankingNamePlate> namePlates = new();

    public RankingSceneState State { get; set; } = RankingSceneState.Global;

    private void Start()
    {
        IGameManager.rankingManager = this;
        LoadingUI.SetActive(true);
        namePlatesRoot.SetActive(false);
        DeactivateButtons();
    }

    private void Update()
    {
        if (!goLobbyBtn.interactable) return;

        if (Input.GetKeyDown(KeyCode.Z))
        {
            goLobbyBtn.onClick.Invoke();
            return;
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            myRankBtn.onClick.Invoke();
            return;
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            goLobbyBtn.onClick.Invoke();
            return;
        }
    }

    public async UniTask Init(IEosService eosService)
    {
        this.eosService = eosService;
        await ShowGlobalRanking();
    }

    public async UniTask ShowGlobalRanking()
    {
        DeactivateButtons();
        namePlatesRoot.SetActive(false);
        LoadingUI.SetActive(true);

        var rankDatas = await eosService.GetRankingDatas();

        namePlates.Clear();
        for (int i = 0; i < rankDatas.Count; i++)
        {
            var namePlate = Instantiate(namePlatePrefab, namePlatesRoot.transform);
            namePlates.Add(namePlate);
            namePlate.SetData(rankDatas[i]);
        }

        namePlatesRoot.SetActive(true);
        LoadingUI.SetActive(false);
        ActivateButtons();
    }

    public async UniTask ShowMyRank()
    {
        DeactivateButtons();
        namePlatesRoot.SetActive(false);
        LoadingUI.SetActive(true);

        namePlatesRoot.SetActive(true);
        LoadingUI.SetActive(false);
        ActivateButtons();
    }

    public void GoTitle()
    {
        State = RankingSceneState.GoLobby;
    }


    void ClearNamePlates()
    {
        foreach(var item in namePlates)
        {
            Destroy(item.gameObject);
        }
        namePlates.Clear();
    }

    void DeactivateButtons()
    {
        globalBtn.interactable = false;
        myRankBtn.interactable = false;
        goLobbyBtn.interactable = false;
    }

    void ActivateButtons()
    {
        globalBtn.interactable = true;
        myRankBtn.interactable = true;
        goLobbyBtn.interactable = true;

        globalBtn.onClick.AddListener(async() =>
        {
            await ShowGlobalRanking();
        });

        myRankBtn.onClick.AddListener(async () =>
        {
            await ShowMyRank();
        });

        goLobbyBtn.onClick.AddListener(() =>
        {
            DeactivateButtons();
            State = RankingSceneState.GoLobby;
        });
    }
}
