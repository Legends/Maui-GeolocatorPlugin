﻿using CoreLocation;
using Foundation;
using GeolocatorPlugin.Abstractions;

namespace GeolocatorPlugin.Platforms.iOS;

internal class GeolocationSingleUpdateDelegate : CLLocationManagerDelegate
{
    bool haveLocation;
    readonly Position position = new Position();

    readonly double desiredAccuracy;
    readonly bool includeHeading;
    readonly TaskCompletionSource<Position> tcs;
    readonly CLLocationManager manager;

    public GeolocationSingleUpdateDelegate(CLLocationManager manager, double desiredAccuracy, bool includeHeading, int timeout, CancellationToken cancelToken)
    {
        this.manager = manager;
        tcs = new TaskCompletionSource<Position>(manager);
        this.desiredAccuracy = desiredAccuracy;
        this.includeHeading = includeHeading;

        if (timeout != Timeout.Infinite)
        {
            Timer t = null;
            t = new Timer(s =>
            {
                if (haveLocation)
                    tcs.TrySetResult(new Position(this.position));
                else
                    tcs.TrySetCanceled();

                StopListening();
                t.Dispose();
            }, null, timeout, 0);
        }

        manager.ShouldDisplayHeadingCalibration += (CLLocationManager locationManager) =>
        {
            locationManager.DismissHeadingCalibrationDisplay();
            return false;
        };

        cancelToken.Register(() =>
        {
            StopListening();
            tcs.TrySetCanceled();
        });
    }

    public Task<Position> Task => tcs?.Task;

    public override void AuthorizationChanged(CLLocationManager manager, CLAuthorizationStatus status)
    {
        // If user has services disabled, we're just going to throw an exception for consistency.
        if (status == CLAuthorizationStatus.Denied || status == CLAuthorizationStatus.Restricted)
        {
            StopListening();
            tcs.TrySetException(new GeolocationException(GeolocationError.Unauthorized));
        }
    }

    public override void Failed(CLLocationManager manager, NSError error)
    {
        switch ((CLError)(int)error.Code)
        {
            case CLError.Network:
                StopListening();
                tcs.SetException(new GeolocationException(GeolocationError.PositionUnavailable));
                break;
            case CLError.LocationUnknown:
                StopListening();
                tcs.TrySetException(new GeolocationException(GeolocationError.PositionUnavailable));
                break;
        }
    }

    public override bool ShouldDisplayHeadingCalibration(CLLocationManager locationManager)
    {
        locationManager.DismissHeadingCalibrationDisplay();
        return false;
    }

    public override void UpdatedLocation(CLLocationManager manager, CLLocation newLocation, CLLocation oldLocation)
    {
        if (newLocation.HorizontalAccuracy < 0)
            return;

        if (haveLocation && newLocation.HorizontalAccuracy > position.Accuracy)
            return;

        position.HasAccuracy = true;
        position.Accuracy = newLocation.HorizontalAccuracy;
        position.HasAltitude = newLocation.VerticalAccuracy > -1;
        position.Altitude = newLocation.Altitude;
        position.AltitudeAccuracy = newLocation.VerticalAccuracy;
        position.HasLatitudeLongitude = newLocation.HorizontalAccuracy > -1;
        position.Latitude = newLocation.Coordinate.Latitude;
        position.Longitude = newLocation.Coordinate.Longitude;
        position.HasSpeed = newLocation.Speed > -1;
        position.Speed = newLocation.Speed;
        if (includeHeading)
        {
            position.HasHeading = newLocation.Course > -1;
            position.Heading = newLocation.Course;
        }
        try
        {
            position.Timestamp = new DateTimeOffset(newLocation.Timestamp.ToDateTime());
        }
        catch (Exception)
        {
            position.Timestamp = DateTimeOffset.UtcNow;
        }
        haveLocation = true;
    }

    private void StopListening()
    {
        manager.StopUpdatingLocation();
    }
}