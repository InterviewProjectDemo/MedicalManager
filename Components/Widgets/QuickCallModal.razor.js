export function watchGps(dotNetRef) {
    if (!navigator.geolocation) {
        dotNetRef.invokeMethodAsync("OnGpsError", 2);
        return 0;
    }

    return navigator.geolocation.watchPosition(
        (position) => {
            dotNetRef.invokeMethodAsync(
                "OnGpsFix",
                position.coords.latitude,
                position.coords.longitude,
                position.coords.accuracy);
        },
        (error) => {
            dotNetRef.invokeMethodAsync("OnGpsError", error.code);
        },
        { enableHighAccuracy: true, timeout: 20000, maximumAge: 0 });
}

export function stopGps(id) {
    if (id)
        navigator.geolocation.clearWatch(id);
}
