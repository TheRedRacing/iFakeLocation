"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { MapView } from "@/components/map/map-view";
import { LocationMarker } from "@/components/map/location-marker";
import { RouteLayer } from "@/components/map/route-layer";
import { TileProviderToggle } from "@/components/map/tile-provider-toggle";
import { DeviceSelectPanel } from "@/components/device/device-select-panel";
import { LocationSearch } from "@/components/location/location-search";
import { SetStopLocationButtons } from "@/components/location/set-stop-location-buttons";
import { RoutePlannerPanel } from "@/components/route/route-planner-panel";
import { RouteStatusIndicator } from "@/components/route/route-status-indicator";
import { MessageDialog } from "@/components/dialogs/message-dialog";
import { DownloadProgressDialog } from "@/components/dialogs/download-progress-dialog";
import { AboutDialog } from "@/components/dialogs/about-dialog";
import { apiClient } from "@/lib/api-client";
import { geocode } from "@/lib/nominatim-client";
import { insertViaPointAtSegment } from "@/lib/route-waypoints";
import { buildWaypointSequence } from "@/lib/route-waypoints";
import { resolveOsrmProfile, route as computeRoute } from "@/lib/osrm-client";
import { useRouteStatusPoll } from "@/lib/use-route-status-poll";
import type { DeviceDto, RoutePointDto } from "@/lib/types";
import type { MapRef } from "@/components/ui/map";

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export function AppShell() {
  const [devices, setDevices] = useState<DeviceDto[]>([]);
  const [selectedUdid, setSelectedUdid] = useState<string | null>(null);
  const [refreshingDevices, setRefreshingDevices] = useState(false);

  const [manualMarker, setManualMarker] = useState<RoutePointDto | null>(null);
  const [useAlternativeTiles, setUseAlternativeTiles] = useState(false);
  const mapRef = useRef<MapRef>(null);

  const [messageDialogText, setMessageDialogText] = useState<string | null>(null);
  const [preparingDevice, setPreparingDevice] = useState(false);
  const [aboutOpen, setAboutOpen] = useState(false);
  const [version, setVersion] = useState<string | null>(null);

  const [routeStart, setRouteStart] = useState<RoutePointDto | null>(null);
  const [routeEnd, setRouteEnd] = useState<RoutePointDto | null>(null);
  const [viaPoints, setViaPoints] = useState<RoutePointDto[]>([]);
  const [routePolyline, setRoutePolyline] = useState<RoutePointDto[]>([]);
  const [planningRoute, setPlanningRoute] = useState(false);
  const [speedKmh, setSpeedKmh] = useState(5);
  const [loop, setLoop] = useState(false);
  const [routeSimulating, setRouteSimulating] = useState(false);

  const { status: routeStatus } = useRouteStatusPoll(selectedUdid, routeSimulating, (result) => {
    if (result.state === "completed" && !loop) {
      setRouteSimulating(false);
    }
  });

  // Initial version + map center-on-home-country, matching the original app's page-load behavior.
  useEffect(() => {
    apiClient
      .getVersion()
      .then((v) => setVersion(v.version))
      .catch(() => setVersion(null));

    apiClient
      .getHomeCountry()
      .then(async (h) => {
        const results = await geocode(h.displayName);
        if (results.length > 0) {
          mapRef.current?.jumpTo({ center: [results[0].lng, results[0].lat], zoom: 4 });
        } else {
          mapRef.current?.jumpTo({ center: [0, 0], zoom: 2 });
        }
      })
      .catch(() => mapRef.current?.jumpTo({ center: [0, 0], zoom: 2 }));
  }, []);

  const handleManualMarkerResult = (point: RoutePointDto) => {
    setManualMarker(point);
    mapRef.current?.flyTo({ center: [point.lng, point.lat], zoom: 13 });
  };

  const refreshDevices = async () => {
    setRefreshingDevices(true);
    try {
      const result = await apiClient.getDevices();
      setDevices(result.devices);
      if (result.devices.length > 0 && !result.devices.some((d) => d.udid === selectedUdid)) {
        setSelectedUdid(result.devices[0].udid);
      }
      if (result.devices.length === 0) {
        setSelectedUdid(null);
      }
    } catch (error) {
      setMessageDialogText(errorMessage(error));
    } finally {
      setRefreshingDevices(false);
    }
  };

  // The backend gates on device readiness (developer-mode toggle + image mount) internally
  // before acting -- there is no separate "check dependencies" step to call first. We just show
  // an indeterminate "Preparing device..." dialog while these potentially-slow calls are in
  // flight (see ARCHITECTURE.md: pymobiledevice3's mount step has no granular progress to poll).
  const handleSetLocation = async (): Promise<boolean> => {
    if (!selectedUdid || !manualMarker) return false;
    setPreparingDevice(true);
    try {
      await apiClient.setLocation(selectedUdid, manualMarker);
      return true;
    } catch (error) {
      setMessageDialogText(errorMessage(error));
      return false;
    } finally {
      setPreparingDevice(false);
    }
  };

  const handleStopLocation = async (): Promise<boolean> => {
    if (!selectedUdid) return false;
    setPreparingDevice(true);
    try {
      await apiClient.stopLocation(selectedUdid);
      return true;
    } catch (error) {
      setMessageDialogText(errorMessage(error));
      return false;
    } finally {
      setPreparingDevice(false);
    }
  };

  const handlePlanRoute = async () => {
    if (!routeStart || !routeEnd) return;
    setPlanningRoute(true);
    try {
      const waypoints = buildWaypointSequence(routeStart, viaPoints, routeEnd);
      const result = await computeRoute(waypoints, resolveOsrmProfile(speedKmh));
      setRoutePolyline(result.points);
    } catch (error) {
      setMessageDialogText(errorMessage(error));
    } finally {
      setPlanningRoute(false);
    }
  };

  const handleInsertViaPoint = async (segmentIndex: number, point: RoutePointDto) => {
    const nextViaPoints = insertViaPointAtSegment(viaPoints, segmentIndex, point);
    setViaPoints(nextViaPoints);

    if (!routeStart || !routeEnd) return;
    setPlanningRoute(true);
    try {
      const waypoints = buildWaypointSequence(routeStart, nextViaPoints, routeEnd);
      const result = await computeRoute(waypoints, resolveOsrmProfile(speedKmh));
      setRoutePolyline(result.points);
    } catch (error) {
      setMessageDialogText(errorMessage(error));
    } finally {
      setPlanningRoute(false);
    }
  };

  const handleStartSimulation = async () => {
    if (!selectedUdid || routePolyline.length < 2) return;
    setPreparingDevice(true);
    try {
      await apiClient.startRoute(selectedUdid, routePolyline, speedKmh, loop);
      setRouteSimulating(true);
    } catch (error) {
      setMessageDialogText(errorMessage(error));
    } finally {
      setPreparingDevice(false);
    }
  };

  const handlePauseRoute = async () => {
    if (!selectedUdid) return;
    try {
      await apiClient.pauseRoute(selectedUdid);
    } catch (error) {
      setMessageDialogText(errorMessage(error));
    }
  };

  const handleResumeRoute = async () => {
    if (!selectedUdid) return;
    try {
      await apiClient.resumeRoute(selectedUdid);
    } catch (error) {
      setMessageDialogText(errorMessage(error));
    }
  };

  const handleStopRoute = async () => {
    if (!selectedUdid) return;
    try {
      await apiClient.stopRoute(selectedUdid);
    } catch (error) {
      setMessageDialogText(errorMessage(error));
    } finally {
      setRouteSimulating(false);
    }
  };

  const handleExit = () => {
    void apiClient.exit().finally(() => window.close());
  };

  const hasDevices = devices.length > 0;

  return (
    <div className="flex min-h-full flex-col">
      <header className="flex items-center gap-4 border-b bg-background p-4">
        <h1 className="mr-auto text-lg font-medium">iFakeLocation</h1>
        <Button variant="outline" onClick={() => setAboutOpen(true)}>
          About
        </Button>
        <Button variant="outline" onClick={handleExit}>
          Exit
        </Button>
      </header>

      <main className="mx-auto w-full max-w-4xl flex-1 space-y-6 p-4">
        <DeviceSelectPanel
          devices={devices}
          selectedUdid={selectedUdid}
          onSelectUdid={setSelectedUdid}
          onRefresh={() => void refreshDevices()}
          refreshing={refreshingDevices}
        />

        <MapView useAlternativeTiles={useAlternativeTiles} onDoubleClick={setManualMarker} mapRef={mapRef}>
          {manualMarker && <LocationMarker position={manualMarker} onMove={setManualMarker} />}
          <RouteLayer
            startPoint={routeStart}
            endPoint={routeEnd}
            viaPoints={viaPoints}
            routePoints={routePolyline}
            currentPosition={routeStatus?.currentPosition}
            onInsertViaPoint={(segmentIndex, point) => void handleInsertViaPoint(segmentIndex, point)}
          />
        </MapView>

        <p className="text-muted-foreground text-sm">
          Double-click anywhere (or drag the existing marker) to manually select a fake location. Drag a point on a
          calculated route to insert a waypoint.
        </p>

        <TileProviderToggle checked={useAlternativeTiles} onCheckedChange={setUseAlternativeTiles} />

        <LocationSearch onResult={handleManualMarkerResult} onError={setMessageDialogText} />

        <SetStopLocationButtons
          canSet={hasDevices && manualMarker !== null}
          canStop={hasDevices}
          onSetLocation={handleSetLocation}
          onStopLocation={handleStopLocation}
        />

        <Separator />

        <RoutePlannerPanel
          onGeocodeStart={setRouteStart}
          onGeocodeEnd={setRouteEnd}
          onError={setMessageDialogText}
          speedKmh={speedKmh}
          onSpeedChange={setSpeedKmh}
          loop={loop}
          onLoopChange={setLoop}
          canPlanRoute={routeStart !== null && routeEnd !== null}
          onPlanRoute={() => void handlePlanRoute()}
          planningRoute={planningRoute}
          canStartSimulation={hasDevices && routePolyline.length >= 2 && !routeSimulating}
          onStartSimulation={() => void handleStartSimulation()}
        />

        {routeSimulating && routeStatus && (
          <RouteStatusIndicator
            status={routeStatus}
            onPause={() => void handlePauseRoute()}
            onResume={() => void handleResumeRoute()}
            onStop={() => void handleStopRoute()}
          />
        )}
      </main>

      <MessageDialog message={messageDialogText} onClose={() => setMessageDialogText(null)} />
      <DownloadProgressDialog preparing={preparingDevice} />
      <AboutDialog open={aboutOpen} onOpenChange={setAboutOpen} version={version} />
    </div>
  );
}
