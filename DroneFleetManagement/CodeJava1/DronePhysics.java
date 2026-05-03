package com.Birds;

public class DronePhysics {
    // ================= DONNÉE DE BASE =================
    public static double DRONE_ALTITUDE_M = 5;// 🔁 CHANGE ICI (en mètres)

    public static void setAltitude(double altitude) {
        DRONE_ALTITUDE_M = altitude;
    }
    public static double getAltitude() {
        return DRONE_ALTITUDE_M;
    }

    // ================= CONSTANTES PHYSIQUES =================
    public static final double ALPHA = 31;        // angle de vu
    public static final double RATIO = 0.95;  // 95%
    public static final double DISTANCE_DE_RECHERCHE = 1000; // m

    // ================= CALCULS =================

    /** Longueur vu au sol */
    public static double lenghtOnGround() {
        return 2*DRONE_ALTITUDE_M*Math.tan(ALPHA*Math.PI/360);
    }

    /** 95% Longueur vu au sol */
    public static double lenghtOnGround95() {
        return lenghtOnGround() * RATIO;
    }

    /** Distance d'un drone*/
    public static double droneDistance() {
        return 6*DISTANCE_DE_RECHERCHE+5*lenghtOnGround95();
    }


    /** Distance total*/
    public static double totalDistance() {
        return (DISTANCE_DE_RECHERCHE/lenghtOnGround95())*DISTANCE_DE_RECHERCHE+((DISTANCE_DE_RECHERCHE/lenghtOnGround95())-1)*lenghtOnGround95();
    }

    /** Espacement entre drones */
    public static double spacing() {
        return 6*lenghtOnGround95();
    }

    /** Nombre de drone*/
    public static int droneCount() {
        return ((int) (totalDistance()/droneDistance())) + 1;
    }
}
