using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ToyMachineFiller))]
public class ToyMachineFillerEditor : Editor
{
    private const string GiftsFolder = "Assets/Models/Gifts";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawDefaultInspector();

        var filler = (ToyMachineFiller)target;
        var validCount = filler.CountValidEntries();
        if (validCount == 0)
            EditorGUILayout.HelpBox("Добавьте хотя бы 1 игрушку с Prefab и Spawn Weight > 0.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("Редкие игрушки (Rarity ближе к 1) будут размещаться ниже.", MessageType.Info);

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Заполнить автомат"))
                Refill(filler);

            if (GUILayout.Button("Очистить заполнение"))
                Clear(filler);
        }

        if (GUILayout.Button("Добавить модели из Assets/Models/Gifts"))
            AddGiftModels(filler);

        if (GUILayout.Button("Распределить Rarity по списку (0 -> 1)"))
            SpreadRarityByOrder(filler);

        serializedObject.ApplyModifiedProperties();
    }

    private static void Refill(ToyMachineFiller filler)
    {
        Undo.RegisterFullObjectHierarchyUndo(filler.gameObject, "Refill Toy Machine");
        filler.Refill();
        MarkDirty(filler);
    }

    private static void Clear(ToyMachineFiller filler)
    {
        Undo.RegisterFullObjectHierarchyUndo(filler.gameObject, "Clear Toy Machine");
        filler.ClearGenerated();
        MarkDirty(filler);
    }

    private static void AddGiftModels(ToyMachineFiller filler)
    {
        Undo.RecordObject(filler, "Add Gift Models");
        var so = new SerializedObject(filler);
        var toys = so.FindProperty("_toys");

        var modelGuids = AssetDatabase.FindAssets("t:Model", new[] { GiftsFolder });
        var added = 0;

        foreach (var guid in modelGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
            if (prefab == null)
                continue;

            if (ContainsPrefab(toys, prefab))
                continue;

            var index = toys.arraySize;
            toys.InsertArrayElementAtIndex(index);
            var entry = toys.GetArrayElementAtIndex(index);

            entry.FindPropertyRelative("Name").stringValue = prefab.name;
            entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            entry.FindPropertyRelative("SpawnWeight").floatValue = 1f;
            entry.FindPropertyRelative("Rarity").floatValue = 0f;

            var scale = entry.FindPropertyRelative("ScaleRange");
            scale.vector2Value = Vector2.one;

            added++;
        }

        so.ApplyModifiedProperties();
        MarkDirty(filler);

        if (added > 0)
            SpreadRarityByOrder(filler);

        if (added == 0)
            Debug.Log("ToyMachineFiller: новые модели в папке Gifts не найдены.", filler);
    }

    private static void SpreadRarityByOrder(ToyMachineFiller filler)
    {
        Undo.RecordObject(filler, "Spread Toy Rarity");
        var so = new SerializedObject(filler);
        var toys = so.FindProperty("_toys");
        var count = toys.arraySize;
        if (count == 0)
            return;

        for (var i = 0; i < count; i++)
        {
            var entry = toys.GetArrayElementAtIndex(i);
            var rarity = count <= 1 ? 0f : (float)i / (count - 1);
            entry.FindPropertyRelative("Rarity").floatValue = rarity;
        }

        so.ApplyModifiedProperties();
        MarkDirty(filler);
    }

    private static bool ContainsPrefab(SerializedProperty toys, GameObject prefab)
    {
        for (var i = 0; i < toys.arraySize; i++)
        {
            var entry = toys.GetArrayElementAtIndex(i);
            var item = entry.FindPropertyRelative("Prefab").objectReferenceValue;
            if (item == prefab)
                return true;
        }

        return false;
    }

    private static void MarkDirty(ToyMachineFiller filler)
    {
        EditorUtility.SetDirty(filler);
        if (filler.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(filler.gameObject.scene);
    }
}
