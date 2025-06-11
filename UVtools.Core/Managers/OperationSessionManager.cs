﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using UVtools.Core.Operations;
using ZLinq;

namespace UVtools.Core.Managers;

public class OperationSessionManager : IList<Operation>
{
    #region Settings

    //public static string FilePath;
    #endregion

    #region Singleton

    private static readonly Lazy<OperationSessionManager> _instanceHolder =
        new(() => []);

    public static OperationSessionManager Instance => _instanceHolder.Value;

    #endregion

    #region Members

    private readonly List<Operation> _operations = [];

    #endregion

    #region Constructor
    private OperationSessionManager()
    {
    }
    #endregion

    #region Methods

    public Operation? Find(Type type)
    {
        return this.AsValueEnumerable().FirstOrDefault(operation => operation.GetType() == type);
    }

    public Operation? Find(Operation fromOperation) => Find(fromOperation.GetType());

    #endregion

    #region List Implementation
    public IEnumerator<Operation> GetEnumerator()
    {
        return _operations.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable) _operations).GetEnumerator();
    }

    public void Add(Operation item)
    {
        if (item is null) return;
        _operations.RemoveAll(operation => operation.GetType() == item.GetType());
        var operation = item.Clone();
        operation.ClearPropertyChangedListeners();
        operation.ClearROIandMasks();
        operation.ImportedFrom = Operation.OperationImportFrom.Session;
        _operations.Add(operation);
    }

    public void Clear()
    {
        _operations.Clear();
    }

    public bool Contains(Operation item)
    {
        return _operations.Contains(item);
    }

    public void CopyTo(Operation[] array, int arrayIndex)
    {
        _operations.CopyTo(array, arrayIndex);
    }

    public bool Remove(Operation item)
    {
        return _operations.Remove(item);
    }

    public int Count => _operations.Count;

    public bool IsReadOnly => ((ICollection<Operation>) _operations).IsReadOnly;

    public int IndexOf(Operation item)
    {
        return _operations.IndexOf(item);
    }

    public void Insert(int index, Operation item)
    {
        if (item is null) return;
        _operations.RemoveAll(operation => operation.GetType() == item.GetType());
        var operation = item.Clone();
        operation.ClearPropertyChangedListeners();
        operation.ClearROIandMasks();
        operation.ImportedFrom = Operation.OperationImportFrom.Session;
        _operations.Insert(index, operation);
    }

    public void RemoveAt(int index)
    {
        _operations.RemoveAt(index);
    }

    public Operation this[int index]
    {
        get => _operations[index];
        set => _operations[index] = value;
    }
    #endregion
}