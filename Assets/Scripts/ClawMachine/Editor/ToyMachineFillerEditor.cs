using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ToyMachineFiller))]
public class ToyMachineFillerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawDefaultInspector();

        var filler = (ToyMachineFiller)target;
        var validCount = filler.CountValidEntries();
        if (validCount == 0)
            EditorGUILayout.HelpBox("Toy Catalog пустой или не назначен. Добавьте записи с id, prefab и scale.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox($"Валидных записей каталога: {validCount}.", MessageType.Info);

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Заполнить автомат"))
                Refill(filler);

            if (GUILayout.Button("Очистить заполнение"))
                Clear(filler);
        }

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

    private static void MarkDirty(ToyMachineFiller filler)
    {
        EditorUtility.SetDirty(filler);
        if (filler.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(filler.gameObject.scene);
    }
}
