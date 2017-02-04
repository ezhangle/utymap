﻿using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.Scene;
using UnityEngine;
using UtyMap.Unity;
using UtyMap.Unity.Data;
using UtyMap.Unity.Infrastructure.Config;
using UtyMap.Unity.Infrastructure.Diagnostic;
using UtyMap.Unity.Utils;
using UtyRx;

namespace Assets.Scenes.Surface.Scripts
{
    class SurfaceCameraController : MonoBehaviour
    {
        private const string TraceCategory = "scene.surface";

        private float _originDistance = 1000;

        private int _minLod = 10;
        private int _maxLod = 10;
        private int _currentLod;
        private QuadKey _currentQuadKey;

        private float _lodStep;

        private float _closestDistance = 5;
        private Vector3 _lastPosition = Vector3.zero;

        private readonly Vector3 _origin = Vector3.zero;
        private GeoCoordinate _geoOrigin = new GeoCoordinate(47.1014, 9.6099);

        public GameObject Pivot;
        public GameObject Planet;

        private HashSet<QuadKey> _loadedQuadKeys = new HashSet<QuadKey>();

        private IMapDataStore _dataStore;
        private IProjection _projection;
        private Stylesheet _stylesheet;

        /// <summary> Performs framework initialization once, before any Start() is called. </summary>
        void Awake()
        {
            var appManager = ApplicationManager.Instance;
            appManager.InitializeFramework(ConfigBuilder.GetDefault(), init => { });

            var trace = appManager.GetService<ITrace>();
            var modelBuilder = appManager.GetService<UnityModelBuilder>();
            appManager.GetService<IMapDataStore>()
               .SubscribeOn(Scheduler.ThreadPool)
               .ObserveOn(Scheduler.MainThread)
               .Subscribe(r => r.Item2.Match(
                               e => modelBuilder.BuildElement(r.Item1, e),
                               m => modelBuilder.BuildMesh(r.Item1, m)),
                          ex => trace.Error(TraceCategory, ex, "cannot process mapdata."),
                          () => trace.Warn(TraceCategory, "stop listening mapdata."));

            _dataStore = appManager.GetService<IMapDataStore>();
            _stylesheet = appManager.GetService<Stylesheet>();
            _projection = new CartesianProjection(_geoOrigin);
        }

        void Start()
        {
            _lodStep = (transform.position.y - _closestDistance) / (_maxLod - _minLod);

            UpdateLod();
        }

        void Update()
        {
            // no movements
            if (_lastPosition == transform.position)
                return;

            _lastPosition = transform.position;

            UpdateLod();

            BuildIfNecessary();

            KeepOrigin();
        }

        void OnGUI()
        {
            GUI.contentColor = Color.red;
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), String.Format("Position: {0}\nGeo:{1}\nLOD: {2}",
                transform.position,
                GeoUtils.ToGeoCoordinate(_geoOrigin, new Vector2(transform.position.x, transform.position.z)),
                _currentLod));
        }

        private void KeepOrigin()
        {
            if (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), _origin) < _originDistance)
                return;

            Vector3 direction = new Vector3(transform.position.x, 0, transform.position.z) -_origin;

            Pivot.transform.position = _origin;
            Planet.transform.position += direction * -1;

            _geoOrigin = GeoUtils.ToGeoCoordinate(_geoOrigin, new Vector2(direction.x, direction.z));
            _projection = new CartesianProjection(_geoOrigin);
        }

        /// <summary> Updates current lod level based on current position. </summary>
        private void UpdateLod()
        {
            var distance = transform.position.y - _closestDistance;
            _currentLod = Mathf.Clamp(_maxLod - (int) Math.Round(distance / _lodStep), _minLod, _maxLod);
        }

        /// <summary> Builds quadkeys if necessary. Decision is based on current position and lod level. </summary>
        private void BuildIfNecessary()
        {
            var oldLod = _currentQuadKey.LevelOfDetail;
            var currentPosition = GeoUtils.ToGeoCoordinate(_geoOrigin, new Vector2(_lastPosition.x, _lastPosition.z));
            _currentQuadKey = GeoUtils.CreateQuadKey(currentPosition, _currentLod);

            // zoom in/out
            if (oldLod != _currentLod)
            {
                foreach (var quadKey in _loadedQuadKeys)
                    SafeDestroy(quadKey.ToString());

                _loadedQuadKeys.Clear();

                foreach (var quadKey in GetNeighbours(_currentQuadKey))
                    BuildQuadKey(Planet, quadKey);
            }
            // pan
            else
            {
                var quadKeys = new HashSet<QuadKey>(GetNeighbours(_currentQuadKey));

                foreach (var quadKey in quadKeys)
                {
                    if (_loadedQuadKeys.Contains(quadKey))
                        continue;

                    BuildQuadKey(Planet, quadKey);
                }

                foreach (var quadKey in _loadedQuadKeys)
                    if (!quadKeys.Contains(quadKey))
                        SafeDestroy(quadKey.ToString());

                _loadedQuadKeys = quadKeys;
            }
        }

        private IEnumerable<QuadKey> GetNeighbours(QuadKey quadKey)
        {
            yield return new QuadKey(quadKey.TileX, quadKey.TileY, quadKey.LevelOfDetail);

            yield return new QuadKey(quadKey.TileX - 1, quadKey.TileY, quadKey.LevelOfDetail);
            yield return new QuadKey(quadKey.TileX - 1, quadKey.TileY + 1, quadKey.LevelOfDetail);
            yield return new QuadKey(quadKey.TileX, quadKey.TileY + 1, quadKey.LevelOfDetail);
            yield return new QuadKey(quadKey.TileX + 1, quadKey.TileY + 1, quadKey.LevelOfDetail);
            yield return new QuadKey(quadKey.TileX + 1, quadKey.TileY, quadKey.LevelOfDetail);
            yield return new QuadKey(quadKey.TileX + 1, quadKey.TileY - 1, quadKey.LevelOfDetail);
            yield return new QuadKey(quadKey.TileX, quadKey.TileY - 1, quadKey.LevelOfDetail);
            yield return new QuadKey(quadKey.TileX - 1, quadKey.TileY - 1, quadKey.LevelOfDetail);
        }

        private void BuildQuadKey(GameObject parent, QuadKey quadKey)
        {
            var tileGameObject = new GameObject(quadKey.ToString());
            tileGameObject.transform.parent = parent.transform;
            _dataStore.OnNext(new Tile(quadKey, _stylesheet, _projection, ElevationDataType.Grid, tileGameObject));
            //QuadTreeSystem.BuildQuadTree(parent, quadKey, _projection);
            _loadedQuadKeys.Add(quadKey);
        }

        private static void SafeDestroy(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
                GameObject.Destroy(go);
        }
    }
}
