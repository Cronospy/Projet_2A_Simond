package com.Birds;

import com.GUI.DrawPanel;
import com.GUI.Sky;

import java.awt.*;
import java.awt.geom.AffineTransform;

public class ScriptedBirdie {

    // ===== POSITION =====
    private double x, y;
    private double prevX, prevY;
    private double distanceTravelled = 0;

    // ===== AFFICHAGE =====
    private final int width = 5;
    private final int height = 8;
    private double orientation = -90;

    // ===== MOUVEMENT =====
    private static final double SPEED = 2.0;
    private double targetX, targetY;

    // ===== SCRIPT =====
    private int step = 0;
    private int cycle = 0;
    private int delay;
    private boolean isReady = false;

    private double spacing = DronePhysics.spacing();
    private double lenght = DronePhysics.lenghtOnGround95();

    // ===== ENVIRONNEMENT =====
    private final Sky sky;
    private final int index;

    public ScriptedBirdie(double startX, double startY, Sky sky, int index) {
        this.x = startX;
        this.y = startY;
        this.prevX = startX;
        this.prevY = startY;
        this.sky = sky;
        this.index = index;

        // --- CALCUL DU DÉLAI ---
        int total = DronePhysics.droneCount();

        int distFromEdge = Math.min(index, (total - 1) - index);

        this.delay = distFromEdge * 20;

        computeTarget();
    }

    // ================= UPDATE =================
    public void updatePosition() {
        if (delay > 0) {
            delay--; // Le drone attend son tour
            return;
        }

        if (step == 1 && !isReady) {
            // On vérifie si TOUS les drones de la liste sont arrivés au step 1
            if (sky.allDronesReachedStep(1)) {
                isReady = true;
            } else {
                return; // On reste immobile en position basse
            }
        }

        prevX = x;
        prevY = y;

        double dx = targetX - x;
        double dy = targetY - y;
        double dist = Math.sqrt(dx * dx + dy * dy);

        if (dist < SPEED) {
            x = targetX;
            y = targetY;
            nextStep();
        } else {
            x += SPEED * dx / dist;
            y += SPEED * dy / dist;
        }

        distanceTravelled += Math.sqrt(
                (x - prevX) * (x - prevX) +
                        (y - prevY) * (y - prevY)
        );
    }

    // Getter pour que Sky puisse vérifier l'état
    public int getStep() { return step; }

    // ================= SCRIPT =================
    private void nextStep() {
        step++;
        computeTarget();
    }

    private void computeTarget() {

        DrawPanel dp = sky.getGui().getDrawPanel();

        int cx = dp.getWidth() / 2;
        int cy = dp.getHeight() / 2;
        int inner = dp.getInnerSquareSize();

        double inX = cx - inner / 2.0;
        double inY = cy - inner / 2.0;

        double bottomY = inY + inner;
        double topY = inY;
        double meter = inner / 1000.0;

        double baseX = inX + lenght * meter/4 + index * spacing * meter;

        // 🔹 Étape 0 : rejoindre sa position
        if (step == 0) {
            targetX = baseX;
            targetY = bottomY;
            orientation = -90;
            return;
        }

        int localStep = (step - 1) % 4;

        if (cycle < 3) {
            switch (localStep) {
                case 0:
                    targetX = x;
                    targetY = topY;
                    orientation = -90;
                    break;
                case 1:
                    targetX = x + lenght * meter;
                    targetY = y;
                    orientation = 0;
                    break;
                case 2:
                    targetX = x;
                    targetY = bottomY;
                    orientation = 90;
                    break;
                case 3:
                    targetX = x + lenght * meter;
                    targetY = y;
                    orientation = 0;
                    cycle++;
                    break;
            }
            return;
        }

        // 🔹 Retour à la base
        targetX = cx;
        targetY = bottomY;
        orientation = 180;
    }

    // ================= DESSIN =================
    public Shape getShape() {
        int[] xPts = {height / 2, -height / 2, -height / 2};
        int[] yPts = {0, width / 2, -width / 2};

        Polygon triangle = new Polygon(xPts, yPts, 3);

        AffineTransform at = new AffineTransform();
        at.translate(x, y);
        at.rotate(Math.toRadians(orientation));

        return at.createTransformedShape(triangle);
    }

    // ================= GETTERS =================
    public double getX() { return x; }
    public double getY() { return y; }
    public double getDistanceTravelled() { return distanceTravelled; }

    public int getIndex() {
        return index;
    }
}