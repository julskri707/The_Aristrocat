using System;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class WorkerAssignment : MonoBehaviour
{
    [Header("Job")]
    public JobType job = JobType.Bauer;

    [Tooltip("Optional: show a custom job display name instead of enum name.")]
    public string jobLabelOverride = "";

    [Header("Assigned Worksite")]
    public ResourceTickBehaviour assignedField;

    [Header("Name Tag (TextMeshPro)")]
    [Tooltip("Drag your TextMeshPro (world space) here. Can be child object above the head.")]
    public TextMeshPro nameTag;

    [Tooltip("If nameTag is empty, it tries to auto-find TextMeshPro in children.")]
    public bool autoFindNameTag = true;

    [Serializable]
    public class JobStyle
    {
        public JobType job;
        [Tooltip("Assign a TMP Font Asset (or leave empty).")]
        public TMP_FontAsset font;

        [Tooltip("Assign a TMP Material Preset (optional).")]
        public Material materialPreset;

        [Tooltip("Text size override (0 = keep current).")]
        public float fontSize = 0f;

        [Tooltip("Text color override (alpha included).")]
        public Color color = Color.white;
    }

    [Header("Per-Job Text Styles")]
    public JobStyle[] jobStyles;

    [Header("Text Format")]
    [Tooltip("Example: {job}: {field}")]
    public string format = "{job}: {field}";

    [Tooltip("Shown when not assigned to a field.")]
    public string unassignedFieldText = "—";

    private void Awake()
    {
        if (nameTag == null && autoFindNameTag)
            nameTag = GetComponentInChildren<TextMeshPro>(true);

        ApplyStyleForJob();
        UpdateNameTag();
    }

    private void OnValidate()
    {
        // Editor live update
        if (nameTag == null && autoFindNameTag)
            nameTag = GetComponentInChildren<TextMeshPro>(true);

        ApplyStyleForJob();
        UpdateNameTag();
    }

    public void AssignTo(ResourceTickBehaviour field)
    {
        assignedField = field;
        UpdateNameTag();
    }

    public void Unassign()
    {
        assignedField = null;
        UpdateNameTag();
    }

    public void SetJob(JobType newJob)
    {
        job = newJob;
        ApplyStyleForJob();
        UpdateNameTag();
    }

    private void UpdateNameTag()
    {
        if (nameTag == null) return;

        string jobText = string.IsNullOrWhiteSpace(jobLabelOverride) ? job.ToString() : jobLabelOverride;
        string fieldText = assignedField != null ? assignedField.gameObject.name : unassignedFieldText;

        nameTag.text = format
            .Replace("{job}", jobText)
            .Replace("{field}", fieldText);
    }

    private void ApplyStyleForJob()
    {
        if (nameTag == null) return;
        if (jobStyles == null) return;

        for (int i = 0; i < jobStyles.Length; i++)
        {
            var s = jobStyles[i];
            if (s == null) continue;
            if (s.job != job) continue;

            if (s.font != null) nameTag.font = s.font;
            if (s.materialPreset != null) nameTag.fontSharedMaterial = s.materialPreset;

            if (s.fontSize > 0.01f) nameTag.fontSize = s.fontSize;

            nameTag.color = s.color;
            break;
        }
    }
}
