using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class WallControlPointProvider_OLD : MonoBehaviour, IControlPointProvider
{
    [Header("Target (auto si vide)")]
    public MonoBehaviour wallDrawInput;

    [Header("Points dans WallDrawInput")]
    public string pointsMemberName = "points";
    public bool pointsAreLocal = false;

    [Header("Rebuild")]
    public string rebuildMethodName = "";
    public bool callRebuildOnSet = true;

    private object _target;
    private Type _targetType;
    private MemberInfo _pointsMember;
    private MethodInfo _rebuildMethod;

    void Awake()
    {
        AutoFindTargetIfNeeded();
        Cache();
    }

    void OnValidate()
    {
        Cache();
    }

    private void AutoFindTargetIfNeeded()
    {
        if (wallDrawInput != null) return;

        var monos = GetComponents<MonoBehaviour>();
        foreach (var m in monos)
        {
            if (m == null) continue;
            if (ReferenceEquals(m, this)) continue;

            if (m.GetType().Name.Contains("WallDrawInput"))
            {
                wallDrawInput = m;
                break;
            }
        }
    }

    private void Cache()
    {
        _target = wallDrawInput;

        if (_target == null)
        {
            _targetType = null;
            _pointsMember = null;
            _rebuildMethod = null;
            return;
        }

        _targetType = _target.GetType();
        _pointsMember = FindFieldOrProperty(_targetType, pointsMemberName);

        _rebuildMethod = string.IsNullOrWhiteSpace(rebuildMethodName)
            ? null
            : FindMethodNoArgs(_targetType, rebuildMethodName);
    }

    public int ControlPointCount => GetPointsList()?.Count ?? 0;

    public Vector3 GetControlPointWorld(int index)
    {
        var list = GetPointsList();
        if (list == null || index < 0 || index >= list.Count) return Vector3.zero;

        Vector3 p = list[index];
        if (pointsAreLocal && wallDrawInput != null)
            p = wallDrawInput.transform.TransformPoint(p);

        return p;
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        var list = GetPointsList();
        if (list == null || index < 0 || index >= list.Count) return;

        Vector3 p = worldPos;
        if (pointsAreLocal && wallDrawInput != null)
            p = wallDrawInput.transform.InverseTransformPoint(worldPos);

        list[index] = p;

        if (callRebuildOnSet)
            TryRebuild();
    }

    public bool IsControlPointEditable(int index)
    {
        var list = GetPointsList();
        return list != null && index >= 0 && index < list.Count;
    }

    private List<Vector3> GetPointsList()
    {
        if (_target == null || _targetType == null) return null;

        if (_pointsMember == null)
            _pointsMember = FindFieldOrProperty(_targetType, pointsMemberName);

        if (_pointsMember == null) return null;

        object value = null;
        if (_pointsMember is FieldInfo fi) value = fi.GetValue(_target);
        else if (_pointsMember is PropertyInfo pi) value = pi.GetValue(_target);

        return value as List<Vector3>;
    }

    private void TryRebuild()
    {
        if (_target == null || _targetType == null) return;
        if (_rebuildMethod == null) return;

        try { _rebuildMethod.Invoke(_target, null); }
        catch { }
    }

    private static MemberInfo FindFieldOrProperty(Type t, string name)
    {
        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) return f;

        var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null) return p;

        return null;
    }

    private static MethodInfo FindMethodNoArgs(Type t, string name)
    {
        return t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
    }
}
