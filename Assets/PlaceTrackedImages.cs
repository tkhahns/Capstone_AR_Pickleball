using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARTrackedImageManager))]
public class PlaceTrackedImages : MonoBehaviour
{
    // Reference to AR tracked image manager component
    private ARTrackedImageManager _trackedImagesManager;

    // List of prefabs to instantiate - these should be named the same
    // as their corresponding 2D images in the reference image library 
    public GameObject[] ArPrefabs;


    // Keep dictionary array of created prefabs
    private readonly Dictionary<string, GameObject> _instantiatedPrefabs = new Dictionary<string, GameObject>();

    void Awake()
    {
        // Cache a reference to the Tracked Image Manager component
        _trackedImagesManager = GetComponent<ARTrackedImageManager>();
    }

    void OnEnable()
    {
        // Attach event handler when tracked images change
        _trackedImagesManager.trackedImagesChanged += OnTrackedImagesChanged;
    }

    void OnDisable()
    {
        // Remove event handler 
        _trackedImagesManager.trackedImagesChanged -= OnTrackedImagesChanged;
    }

    // ── Public Reset API (called by RecalibrateUI) ────────────────

    /// <summary>
    /// Destroys all spawned racket prefabs so the next QR detection will re-spawn them.
    /// </summary>
    public void ResetRacket()
    {
        var toRemove = new List<string>();
        foreach (var kvp in _instantiatedPrefabs)
        {
            if (kvp.Value != null) Destroy(kvp.Value);
            toRemove.Add(kvp.Key);
        }
        foreach (var key in toRemove)
            _instantiatedPrefabs.Remove(key);

        Debug.Log("[PlaceTrackedImages] Racket prefabs cleared — scan QR again.");
    }

    // Event Handler
    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        // Process both newly added AND updated images
        ProcessTrackedImages(eventArgs.added);
        ProcessTrackedImages(eventArgs.updated);

        // If the AR subsystem has given up looking for a tracked image
        foreach (var trackedImage in eventArgs.removed)
        {
            var imageName = trackedImage.referenceImage.name;

            if (_instantiatedPrefabs.TryGetValue(imageName, out var go))
            {
                Destroy(go);
                _instantiatedPrefabs.Remove(imageName);
            }
        }
    }

    private void ProcessTrackedImages(IReadOnlyList<ARTrackedImage> images)
    {
        if (images == null) return;

        foreach (var trackedImage in images)
        {
            var imageName = trackedImage.referenceImage.name;

            // ── Normal prefab-spawning for tracked images (e.g. racket) ──
            foreach (var curPrefab in ArPrefabs)
            {
                if (string.Compare(curPrefab.name, imageName, StringComparison.OrdinalIgnoreCase) == 0
                    && !_instantiatedPrefabs.ContainsKey(imageName))
                {
                    var newPrefab = Instantiate(curPrefab, trackedImage.transform);
                    _instantiatedPrefabs[imageName] = newPrefab;
                }
            }

            // Update visibility of already-instantiated prefabs
            if (_instantiatedPrefabs.TryGetValue(imageName, out var existingGo))
            {
                existingGo.SetActive(trackedImage.trackingState == TrackingState.Tracking);
            }
        }
    }
}