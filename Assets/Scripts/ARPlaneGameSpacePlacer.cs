using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARPlaneGameSpacePlacer : MonoBehaviour
{
    [Header("Game Space")]
    [Tooltip("Root object that contains the whole game world (court, bots, ball spawners, etc.).")]
    [SerializeField] private Transform gameSpaceRoot;

    [SerializeField] private bool hideGameSpaceUntilPlaced = true;

    [Header("AR References")]
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private Camera arCamera;

    [Header("Placement")]
    [SerializeField] private bool autoPlaceOnFirstDetectedPlane = true;
    [Tooltip("When true, placement is deferred until AllowPlacement() is called " +
             "(e.g. by the PlaceTrackedImages.onFirstImageDetected event). " +
             "Planes are still detected in the background so a surface is ready.")]
    [SerializeField] private bool waitForExternalTrigger = false;
    [SerializeField] private bool requireTapToPlace = false;
    [SerializeField] private bool allowRepositionAfterPlacement = false;
    [SerializeField] private Vector3 placementOffsetMeters = Vector3.zero;
    [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;
    [SerializeField] private bool alignYawToCamera = true;

    [Header("Camera Height")]
    [Tooltip("Assumed player eye-height in metres. Used by the fallback " +
             "(no-plane) path to place the court this far below the camera.")]
    [SerializeField] private float playerHeight = 1.7f;

    [Header("After Placement")]
    [SerializeField] private bool disablePlaneDetectionAfterPlacement = true;
    [SerializeField] private bool disablePlaneVisualsAfterPlacement = true;

    private static readonly List<ARRaycastHit> RaycastHits = new List<ARRaycastHit>();

    private bool isPlaced;
    private bool isAllowed;        // external trigger received
    private Pose? pendingPlanePose; // best plane pose stored while waiting

    private void Awake()
    {
        ResolveReferences();

        if (gameSpaceRoot != null && hideGameSpaceUntilPlaced)
        {
            gameSpaceRoot.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged += OnPlanesChanged;
        }
    }

    private void OnDisable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    private void Update()
    {
        if (!requireTapToPlace)
        {
            return;
        }

        if (isPlaced && !allowRepositionAfterPlacement)
        {
            return;
        }

        if (Input.touchCount == 0)
        {
            return;
        }

        Touch touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began)
        {
            return;
        }

        if (raycastManager == null)
        {
            return;
        }

        if (raycastManager.Raycast(touch.position, RaycastHits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = RaycastHits[0].pose;
            PlaceGameSpace(hitPose.position, hitPose.rotation);
        }
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (!autoPlaceOnFirstDetectedPlane || requireTapToPlace)
        {
            return;
        }

        if (isPlaced && !allowRepositionAfterPlacement)
        {
            return;
        }

        if (args.added == null || args.added.Count == 0)
        {
            return;
        }

        ARPlane plane = args.added[0];
        Pose planePose = new Pose(plane.transform.position, plane.transform.rotation);

        // If we must wait for an external trigger (e.g. image detection), store the pose
        if (waitForExternalTrigger && !isAllowed)
        {
            pendingPlanePose = planePose;
            return;
        }

        PlaceGameSpace(planePose.position, planePose.rotation);
    }

    /// <summary>
    /// Called by PlaceTrackedImages when the court anchor QR code is detected.
    /// Places the game space so that the anchor QR sits at the given world pose,
    /// applying placementOffsetMeters to shift the court into the correct position
    /// relative to the anchor.
    /// </summary>
    public void PlaceAtAnchor(Pose anchorPose)
    {
        if (isPlaced) return;

        // Use the anchor's yaw (flat on the floor), ignore pitch/roll
        Vector3 anchorForward = anchorPose.rotation * Vector3.forward;
        anchorForward.y = 0f;
        if (anchorForward.sqrMagnitude < 0.0001f)
            anchorForward = Vector3.forward;

        Quaternion targetRotation = Quaternion.LookRotation(anchorForward.normalized, Vector3.up);
        targetRotation *= Quaternion.Euler(rotationOffsetEuler);

        // Anchor position is at floor level; offset shifts GameSpaceRoot
        // so the court mesh (which has large negative-Y local positions) lands correctly.
        Vector3 targetPosition = anchorPose.position + targetRotation * placementOffsetMeters;

        FinalizePlace(targetPosition, targetRotation);
    }

    /// <summary>
    /// Call this from PlaceTrackedImages.onFirstImageDetected (or any other trigger)
    /// to allow the game space to be placed. If a plane was already detected,
    /// placement happens immediately; otherwise it happens on the next plane detection.
    /// </summary>
    public void AllowPlacement()
    {
        isAllowed = true;

        // If a plane was already found while we were waiting, place now
        if (pendingPlanePose.HasValue && !isPlaced)
        {
            PlaceGameSpace(pendingPlanePose.Value.position, pendingPlanePose.Value.rotation);
        }
        // If no plane yet but we want instant feedback, fall back to camera-forward on the floor
        else if (!isPlaced && arCamera != null)
        {
            // Place GameSpaceRoot directly beneath the camera (offset handles court positioning)
            Vector3 fallbackPos = arCamera.transform.position;
            fallbackPos.y = arCamera.transform.position.y - playerHeight; // floor level
            PlaceGameSpace(fallbackPos, Quaternion.identity);
        }
    }

    /// <summary>
    /// Plane-based placement: computes final rotation (camera-aligned or plane-based)
    /// and applies offsets, then calls FinalizePlace.
    /// </summary>
    private void PlaceGameSpace(Vector3 planePosition, Quaternion planeRotation)
    {
        if (gameSpaceRoot == null)
        {
            return;
        }

        Quaternion targetRotation;
        if (alignYawToCamera && arCamera != null)
        {
            Vector3 cameraForward = arCamera.transform.forward;
            cameraForward.y = 0f;
            if (cameraForward.sqrMagnitude < 0.0001f)
            {
                cameraForward = Vector3.forward;
            }

            targetRotation = Quaternion.LookRotation(cameraForward.normalized, Vector3.up);
        }
        else
        {
            targetRotation = planeRotation;
        }

        targetRotation *= Quaternion.Euler(rotationOffsetEuler);

        Vector3 targetPosition = planePosition + targetRotation * placementOffsetMeters;
        FinalizePlace(targetPosition, targetRotation);
    }

    /// <summary>
    /// Shared final step: actually moves GameSpaceRoot, activates it, and disables
    /// plane detection / visuals if configured.
    /// </summary>
    private void FinalizePlace(Vector3 finalPosition, Quaternion finalRotation)
    {
        if (gameSpaceRoot == null) return;

        gameSpaceRoot.SetPositionAndRotation(finalPosition, finalRotation);

        if (!gameSpaceRoot.gameObject.activeSelf)
        {
            gameSpaceRoot.gameObject.SetActive(true);
        }

        isPlaced = true;

        if (disablePlaneVisualsAfterPlacement)
        {
            SetPlaneVisualsEnabled(false);
        }

        if (disablePlaneDetectionAfterPlacement && planeManager != null)
        {
            planeManager.enabled = false;
        }
    }

    private void ResolveReferences()
    {
        if (planeManager == null)
        {
            planeManager = FindFirstObjectByType<ARPlaneManager>();
        }

        if (raycastManager == null)
        {
            raycastManager = FindFirstObjectByType<ARRaycastManager>();
        }

        if (arCamera == null)
        {
            arCamera = Camera.main;
        }
    }

    private void SetPlaneVisualsEnabled(bool isEnabled)
    {
        if (planeManager == null)
        {
            return;
        }

        foreach (ARPlane plane in planeManager.trackables)
        {
            MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = isEnabled;
            }

            LineRenderer lineRenderer = plane.GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                lineRenderer.enabled = isEnabled;
            }
        }
    }
}