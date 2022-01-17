using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Robust.Shared.Map;

internal partial class MapManager
{
    // TODO: Move IMapManager stuff to the system
    private Dictionary<MapId, B2DynamicTree<MapGrid>> _gridTrees = new();
    private Dictionary<MapId, HashSet<IMapGrid>> _movedGrids = new();

    // Gets the grids that have moved this tick until broadphase has run.
    public HashSet<IMapGrid> GetMovedGrids(MapId mapId)
    {
        return _movedGrids[mapId];
    }

    public void ClearMovedGrids(MapId mapId)
    {
        _movedGrids[mapId].Clear();
    }

    private void StartupGridTrees()
    {
        MapCreated += OnMapCreatedGridTree;
        MapDestroyed += OnMapDestroyedGridTree;

        _entityManager.EventBus.SubscribeEvent<GridInitializeEvent>(EventSource.Local, this, OnGridInit);
        _entityManager.EventBus.SubscribeEvent<GridRemovalEvent>(EventSource.Local, this, OnGridRemove);
        _entityManager.EventBus.SubscribeLocalEvent<MapGridComponent, MoveEvent>(OnGridMove);
        _entityManager.EventBus.SubscribeLocalEvent<MapGridComponent, EntMapIdChangedMessage>(OnGridMapChange);
        _entityManager.EventBus.SubscribeLocalEvent<MapGridComponent, GridFixtureChangeEvent>(OnGridBoundsChange);
    }

    private void ShutdownGridTrees()
    {
        MapCreated -= OnMapCreatedGridTree;
        MapDestroyed -= OnMapDestroyedGridTree;

        _entityManager.EventBus.UnsubscribeEvent<GridInitializeEvent>(EventSource.Local, this);
        _entityManager.EventBus.UnsubscribeEvent<GridRemovalEvent>(EventSource.Local, this);
        _entityManager.EventBus.UnsubscribeLocalEvent<MapGridComponent, MoveEvent>();
        _entityManager.EventBus.UnsubscribeLocalEvent<MapGridComponent, EntMapIdChangedMessage>();
        _entityManager.EventBus.UnsubscribeLocalEvent<MapGridComponent, GridFixtureChangeEvent>();
    }

    private void OnMapCreatedGridTree(object? sender, MapEventArgs e)
    {
        if (e.Map == MapId.Nullspace) return;

        _gridTrees.Add(e.Map, new B2DynamicTree<MapGrid>());
        _movedGrids.Add(e.Map, new HashSet<IMapGrid>());
    }

    private void OnMapDestroyedGridTree(object? sender, MapEventArgs e)
    {
        if (e.Map == MapId.Nullspace) return;

        _gridTrees.Remove(e.Map);
        _movedGrids.Remove(e.Map);
    }

    private Box2 GetWorldAABB(MapGrid grid)
    {
        var xform = EntityManager.GetComponent<TransformComponent>(grid.GridEntityId);

        var (worldPos, worldRot) = xform.GetWorldPositionRotation();

        return new Box2Rotated(grid.LocalBounds.Translated(worldPos), worldRot, worldPos).CalcBoundingBox();
    }

    private void OnGridInit(GridInitializeEvent args)
    {
        var grid = (MapGrid) GetGrid(args.GridId);
        var xform = EntityManager.GetComponent<TransformComponent>(args.EntityUid);
        var mapId = xform.MapID;

        var aabb = GetWorldAABB(grid);
        var proxy = _gridTrees[mapId].CreateProxy(in aabb, grid);

        grid.MapProxy = proxy;

        _movedGrids[mapId].Add(grid);
    }

    private void OnGridRemove(GridRemovalEvent args)
    {
        var grid = (MapGrid) GetGrid(args.GridId);
        var xform = EntityManager.GetComponent<TransformComponent>(args.EntityUid);

        _gridTrees[xform.MapID].DestroyProxy(grid.MapProxy);
    }

    private void OnGridMove(EntityUid uid, MapGridComponent component, ref MoveEvent args)
    {
        var xform = EntityManager.GetComponent<TransformComponent>(uid);
        var grid = (MapGrid) component.Grid;
        var aabb = GetWorldAABB(grid);
        _gridTrees[xform.MapID].MoveProxy(grid.MapProxy, in aabb, Vector2.Zero);
    }

    private void OnGridMapChange(EntityUid uid, MapGridComponent component, EntMapIdChangedMessage args)
    {
        // Make sure we cleanup old map for moved grid stuff.
        var mapId = EntityManager.GetComponent<TransformComponent>(uid).MapID;

        if (_movedGrids.TryGetValue(args.OldMapId, out var oldMovedGrids))
        {
            oldMovedGrids.Remove(component.Grid);
        }

        if (_movedGrids.TryGetValue(mapId, out var newMovedGrids))
        {
            newMovedGrids.Add(component.Grid);
        }
    }

    private void OnGridBoundsChange(EntityUid uid, MapGridComponent component, GridFixtureChangeEvent args)
    {
        // Just MapLoader things.
        if (EntityManager.GetComponent<MetaDataComponent>(uid).EntityLifeStage < EntityLifeStage.Initialized) return;

        var xform = EntityManager.GetComponent<TransformComponent>(uid);
        var grid = (MapGrid) component.Grid;
        var aabb = GetWorldAABB(grid);
        _gridTrees[xform.MapID].MoveProxy(grid.MapProxy, in aabb, Vector2.Zero);
    }
}
