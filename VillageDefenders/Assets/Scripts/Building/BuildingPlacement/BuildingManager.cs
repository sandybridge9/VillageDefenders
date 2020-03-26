﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Windows.WebCam;

public class BuildingManager : MonoBehaviour
{
    #region Fields

    private BuildingCollisionManager currentBuildingCollisionManager; //Used to detect if there is already a building in place
    private Building currentBuilding; //Current building that needs to be placed -> for instantiating
    private Building currentBuildingSelection; //Current building for checking placement position, changing color etc.
    private HeightChecking currentBuildingHeightChecking; //Responsible for checking height of currently selected building

    private List<Renderer> currentlySelectedBuildingRenderers;
    private Material materialCanBuild;
    private Material materialCantBuild;

    private float tileSize = 0.5f; // Grid snapping step size
    private float rotationDelay = 60f; // Used to delay rotation
    private float cancelDelay = 60f; // Used to delay canceling
    private float placementDelay = 120f;
    private LayerMask groundLayerMask;

    private AxisLock? WallBuildingAxisLock;
    private bool creatingWall;
    private Building lastWall = null;

    #endregion

    #region Properties

    private List<Building> placeableBuildings;

    #endregion

    #region Overriden Methods

    void Start()
    {
        placeableBuildings = SettingsManager.Instance.PlaceableBuildings;
        materialCanBuild = SettingsManager.Instance.MaterialCanBuild;
        materialCantBuild = SettingsManager.Instance.MaterialCantBuild;
        currentlySelectedBuildingRenderers = new List<Renderer>();
    }

    void Update()
    {
        if (currentBuilding != null)
        {
            MoveCurrentObjectToMouse();
            ChangeColor();
            RotateBuilding();
            BuildingPlacement();
        }
        CancelSelection();
    }

    void OnGUI()
    {
        for (int i = 0; i < placeableBuildings.Count; i++)
        {
            if (GUI.Button(new Rect(Screen.width / 20, Screen.height / 15 + Screen.height / 12 * i, 100, 30),
                placeableBuildings[i].name))
            {
                SetItem(placeableBuildings[i]);
            }
        }
    }

    #endregion

    #region HelperMethods

    //Checks if current building position contains any other buildings and if building is on terrain that is even enough for placement
    bool IsPositionViable()
    {
        if (!currentBuildingCollisionManager.IsColliding() && currentBuildingHeightChecking.CanPlace)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    //Sets currently selected building
    public void SetItem(Building b)
    {
        CleanUp();
        currentBuilding = b;
        currentBuildingSelection = Instantiate(b);
        //TODO fix wall transparent state
        //currentBuildingRenderer = currentBuilding.GetComponent<Renderer>();

        currentBuildingCollisionManager = currentBuildingSelection.GetComponent<BuildingCollisionManager>();
        if (currentBuildingSelection.GetComponent<HeightChecking>() != null)
        {
            currentBuildingSelection.transform.GetComponent<HeightChecking>().enabled = true;
            currentBuildingHeightChecking = currentBuildingSelection.GetComponent<HeightChecking>();
        }
        GetMeshRenderersOfCurrentBuilding();
    }

    private void MoveCurrentObjectToMouse()
    {
        groundLayerMask = SettingsManager.Instance.GroundLayerMask;
        RaycastHit hitInfo;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out hitInfo, 10000f, groundLayerMask))
        {
            //Snapping
            if (currentBuildingSelection.BuildingType == BuildingTypes.WoodenWall)
            {
                var wallTileSize = currentBuildingSelection.transform.Find("Wood Wall").Find("Wall").Find("Mesh")
                    .GetComponent<MeshRenderer>().bounds.size.z;

                if (WallBuildingAxisLock != null)
                {
                    if (WallBuildingAxisLock.Value.Axis == 'x')
                    {
                        currentBuildingSelection.transform.position = new Vector3(
                            WallBuildingAxisLock.Value.Value,
                            currentBuildingSelection.transform.position.y, //Position Y is set in HeightChecking script
                            Mathf.Floor(hitInfo.point.z / wallTileSize) * wallTileSize);
                    }
                    else
                    {
                        currentBuildingSelection.transform.position = new Vector3(
                            Mathf.Floor(hitInfo.point.x / wallTileSize) * wallTileSize,
                            currentBuildingSelection.transform.position.y, //Position Y is set in HeightChecking script
                            WallBuildingAxisLock.Value.Value);
                    }
                }
                else
                {
                    currentBuildingSelection.transform.position = new Vector3(
                        Mathf.Floor(hitInfo.point.x / wallTileSize) * wallTileSize,
                        currentBuildingSelection.transform.position.y, //Position Y is set in HeightChecking script
                        Mathf.Floor(hitInfo.point.z / wallTileSize) * wallTileSize);
                }
            }
            else
            {
                currentBuildingSelection.transform.position = new Vector3(
                    Mathf.Floor(hitInfo.point.x / tileSize) * tileSize,
                    currentBuildingSelection.transform.position.y, //Position Y is set in HeightChecking script
                    Mathf.Floor(hitInfo.point.z / tileSize) * tileSize);
            }
        }
    }

    private void RotateBuilding()
    {
        if (Input.GetKey(KeyCode.R))
        {
            if (rotationDelay >= 60)
            {
                currentBuildingSelection.transform.Rotate(Vector3.up, -90);
                rotationDelay = 0;
            }
        }
        else if (Input.GetKey(KeyCode.T))
        {
            if (rotationDelay >= 60)
            {
                currentBuildingSelection.transform.Rotate(Vector3.up, 90);
                rotationDelay = 0;
            }
        }

        rotationDelay++;
    }

    private void BuildingPlacement()
    {
        if (currentBuildingSelection.BuildingType == BuildingTypes.WoodenWall)
        {
            //PlaceSpammableBuilding();
            PlaceWall();
        }
        else if (currentBuildingSelection.BuildingType == BuildingTypes.Townhall)
        {
            PlaceUniqueBuilding();
        }
        else
        {
            PlaceBuilding();
        }
        placementDelay++;
    }

    //Axis lock is used for wall building, to make straight line wall buiilding easier.
    struct AxisLock
    {
        public char Axis { get; set; }
        public float Value { get; set; }

        public AxisLock(char axis, float value)
        {
            this.Axis = axis;
            this.Value = value;
        }
    }

    private void PlaceWall()
    {
        //If user is holding x key down -> Lock X axis
        if (Input.GetKey(KeyCode.X))
        {
            if (WallBuildingAxisLock == null || WallBuildingAxisLock.Value.Axis == 'z')
            {
                WallBuildingAxisLock = new AxisLock('x', currentBuildingSelection.transform.position.x);
            }
        }
        //If user is holding z key down -> Lock Z axis
        else if (Input.GetKey(KeyCode.Z))
        {
            if (WallBuildingAxisLock == null || WallBuildingAxisLock.Value.Axis == 'x')
            {
                WallBuildingAxisLock = new AxisLock('z', currentBuildingSelection.transform.position.z);
            }
        }
        // User has released x/z buttons, reset lock
        else
        {
            WallBuildingAxisLock = null;
        }

        if (Input.GetKey(KeyCode.Mouse0))
        {
            if (IsPositionViable())
            {
                if (WallBuildingAxisLock != null)
                {
                    if (WallBuildingAxisLock.Value.Axis == 'x')
                    {
                        BuildBuilding(WallBuildingAxisLock.Value.Value,
                            currentBuildingHeightChecking.OptimalHeight,
                            currentBuildingSelection.transform.position.z,
                            currentBuildingSelection.transform.rotation);
                    }
                    else if (WallBuildingAxisLock.Value.Axis == 'z')
                    {
                        BuildBuilding(currentBuildingSelection.transform.position.x,
                            currentBuildingHeightChecking.OptimalHeight,
                            WallBuildingAxisLock.Value.Value,
                            currentBuildingSelection.transform.rotation);
                    }
                }
                else
                {
                    BuildBuilding(currentBuildingSelection.transform.position.x,
                        currentBuildingHeightChecking.OptimalHeight,
                        currentBuildingSelection.transform.position.z,
                        currentBuildingSelection.transform.rotation);
                }
            }
        }
    }

    private void PlaceBuilding()
    {
        if (Input.GetKey(KeyCode.Mouse0) && IsPositionViable() && placementDelay >= 120f)
        {
            BuildBuilding(currentBuildingSelection.transform.position.x,
                currentBuildingHeightChecking.OptimalHeight,
                currentBuildingSelection.transform.position.z,
                currentBuildingSelection.transform.rotation);
            placementDelay = 0;
        }
    }

    //Placement for unique building(Buildings that you can't have several of)
    private void PlaceUniqueBuilding()
    {
        if (Input.GetKey(KeyCode.Mouse0) && IsPositionViable())
        {
            BuildBuilding(currentBuildingSelection.transform.position.x,
                currentBuildingHeightChecking.OptimalHeight,
                currentBuildingSelection.transform.position.z,
                currentBuildingSelection.transform.rotation);
            CleanUp();
        }
    }

    //Method responsible for instantiating a new building and setting up its components
    private void BuildBuilding(float x, float y, float z, Quaternion rotation)
    {
        Debug.Log(x+" "+y+" "+z);
        BuildBuilding(x, y, z, rotation, currentBuilding);
    }

    //Method responsible for instantiating a new building and setting up its components
    private void BuildBuilding(float x, float y, float z, Quaternion rotation, Building building)
    {
        var newCopy = GameObject.Instantiate(building, new Vector3(x,y,z), rotation);
        newCopy.IsPlaced = true;
        newCopy.GetComponent<HeightChecking>().enabled = false;
        newCopy.GetComponent<BuildingCollisionManager>().enabled = false;
    }

    private void CancelSelection()
    {
        if (Input.GetKey(KeyCode.C) && cancelDelay >= 60 && currentBuildingSelection != null)
        {
            CleanUp();
            cancelDelay = 0;
        }
        cancelDelay++;
    }

    public void CleanUp()
    {
        currentBuildingHeightChecking = null;
        currentBuildingCollisionManager = null;
        currentlySelectedBuildingRenderers = new List<Renderer>();
        if(currentBuilding != null)
        {
            currentBuildingSelection.Destroy();
            currentBuildingSelection = null;
            currentBuilding = null;
        }
    }

    public bool HasSelectedBuilding()
    {
        return currentBuildingSelection != null;
    }

    //To get Renderer component from Prefabs, that have main renderer attached to one of its child objects instead of parent
    private void GetMeshRenderersOfCurrentBuilding()
    {
        Renderer parentRenderer = currentBuildingSelection.GetComponent<Renderer>();
        //If parent object doesn't have a renderer, search in children
        if(parentRenderer == null)
        {
            currentlySelectedBuildingRenderers = currentBuildingSelection.GetComponentsInChildren<Renderer>().ToList();
        }
        else
        {
            //Add parent object renderer
            currentlySelectedBuildingRenderers.Add(parentRenderer);
        }
    }

    //NOTE TO SELF: Implement differently if performance drops
    private void ChangeColor()
    {
        //If position is clear and the height is good - set green color for all materials in all renderers
        if (IsPositionViable())
        {
            foreach(var renderer in currentlySelectedBuildingRenderers)
            {
                int size = renderer.materials.Length;
                Material[] newMaterials = new Material[size];
                for (int i = 0; i < size; i++)
                {
                    newMaterials[i] = materialCanBuild;
                }
                renderer.materials = newMaterials;
            }
        }
        //Else - set red color for all materials in all renderers
        else
        {
            foreach(var renderer in currentlySelectedBuildingRenderers)
            {
                int size = renderer.materials.Length;
                Material[] newMaterials = new Material[size];
                for (int i = 0; i < size; i++)
                {
                    newMaterials[i] = materialCantBuild;
                }
                renderer.materials = newMaterials;
            }
        }
    }

    #endregion
}
