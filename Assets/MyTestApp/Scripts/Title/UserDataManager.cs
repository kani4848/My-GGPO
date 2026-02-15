using UnityEngine;
using System.IO;
using System;

public static class UserDataManager
{
    private static string SavePath => Path.Combine(Application.persistentDataPath, "userData.json");

    // 保存
    public static void SaveUserName(string name)
    {
        UserData data = new UserData();
        data.userName = name;

        string json = JsonUtility.ToJson(data, true); // trueで整形

        File.WriteAllText(SavePath, json);
    }

    // 読み込み
    public static string LoadUserName()
    {
        if (!File.Exists(SavePath))
        {
            return null; // 保存がない場合
        }

        string json = File.ReadAllText(SavePath);

        UserData data = JsonUtility.FromJson<UserData>(json);

        return data.userName;
    }
}

[Serializable]
public class UserData
{
    public string userName;
}
