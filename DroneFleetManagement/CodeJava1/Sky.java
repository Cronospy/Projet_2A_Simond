package com.GUI;

import com.Birds.DronePhysics;
import com.Birds.ScriptedBirdie;

import javax.swing.*;
import java.awt.*;
import java.util.ArrayList;

public class Sky extends JPanel {

    private final ArrayList<ScriptedBirdie> birds = new ArrayList<>();
    private final GUI gui;

    // Variable pour stocker les coordonnées trouvées
    private String detectionMessage = "Target : searching...";
    private String detectioncoor = "X=0, Y=0";

    private int currentDroneCount;

    public Sky(GUI gui) {
        this.gui = gui;
        setOpaque(false);
        reset();
    }

    public GUI getGui() {
        return gui;
    }


    // ===================== PAINT =====================

    @Override
    protected void paintComponent(Graphics g) {
        super.paintComponent(g);

        Graphics2D g2 = (Graphics2D) g.create();
        g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING,
                RenderingHints.VALUE_ANTIALIAS_ON);

        DrawPanel dp = gui.getDrawPanel();
        double kmPerPixel = dp.getKmPerPixel();

        double maxDistM = 0;
        double minDistM = Double.MAX_VALUE;

        for (ScriptedBirdie bird : birds) {
            // Dessiner l'oiseau
            g2.setColor(Color.BLACK);
            g2.fill(bird.getShape());

            // Distance parcourue en mètres
            double distM = bird.getDistanceTravelled() * 1000 * kmPerPixel;

            if (distM > maxDistM) {
                maxDistM = distM;
            }
            if (distM < minDistM) {
                minDistM = distM;
            }

            // Afficher uniquement la distance au-dessus de chaque oiseau
            g2.setFont(new Font("Arial", Font.BOLD, 12));
            g2.drawString(
                    String.format("%.0f m", distM),
                    (int) bird.getX(),
                    (int) bird.getY() - 10
            );
        }

        double speedKMH = 20.0;

        // temps
        double timeHours = (maxDistM / 1000.0) / speedKMH;
        double timeSecondsTotal = timeHours * 3600;

        int minutes = (int) (timeSecondsTotal / 60);
        int seconds = (int) (timeSecondsTotal % 60);

        g2.setColor(Color.WHITE);

        // textes
        String droneText = "Number of drones : " + currentDroneCount;
        String speedText = String.format("Speed : %.1f km/h", speedKMH);
        String timeText = String.format("Time : %d min %d s", minutes, seconds);
        String distText = String.format("Distance (min / max) : %.0f m / %.0f m", minDistM, maxDistM);
        String realCoords = String.format("Coordinates : X = %.1f m ; Y = %.1f m", dp.getRealTargetX_Meters(), dp.getRealTargetY_Meters());

        g2.setFont(new Font("Arial", Font.BOLD, 14));

        // position bas gauche
        int x = 10;
        int y = getHeight() - 90;

        g2.drawString(droneText, x, y );
        g2.drawString(speedText, x, y + 18);
        g2.drawString(timeText, x, y + 36);
        g2.drawString(distText, x, y + 54);
        g2.drawString(realCoords, x, y + 72);

        // --- NOUVEAU : Affichage du message de détection ---
        g2.setColor(dp.isTargetFound() ? new Color(255, 255, 255) : Color.WHITE);
        g2.setFont(new Font("Arial", Font.BOLD, 16));
        FontMetrics fm = g2.getFontMetrics();
        int msgWidth = fm.stringWidth(detectionMessage);

        // Position : Bas Droite
        g2.drawString(detectionMessage, getWidth() - msgWidth - 30, getHeight() - 40);
        g2.drawString(detectioncoor, getWidth() - msgWidth - 30, getHeight() - 20);

        g2.dispose();
    }

    // ===================== UPDATE =====================
    public void update() {
        DrawPanel dp = gui.getDrawPanel();

        // Coordonnées de la cible (pixels canvas)
        int tx = dp.getTargetX();
        int ty = dp.getTargetY();

        // Largeur de vue L en pixels
        double L = com.Birds.DronePhysics.lenghtOnGround();
        int viewRadiusPx = (int)((L / 1000.0) * dp.getInnerSquareSize());


        for (ScriptedBirdie bird : birds) {
            bird.updatePosition();

            //--- LOGIQUE DE DÉTECTION ---
            // Position du drone relative au canvas
            int bx = (int)(bird.getX() - (dp.getWidth()/2 - dp.getInnerSquareSize()/2));
            int by = (int)(bird.getY() - (dp.getHeight()/2 - dp.getInnerSquareSize()/2));

            // Calcul de la distance Drone <-> Cible
            double dist = Math.sqrt(Math.pow(bx - tx, 2) + Math.pow(by - ty, 2));

            if (dist < viewRadiusPx) {
                dp.setTargetFound(true); // La cible devient verte

                // Calcul des coordonnées
                double gpsX = (tx / (double) dp.getInnerSquareSize()) * 1000.0;
                double gpsY = (1.0 - (ty / (double) dp.getInnerSquareSize())) * 1000.0; // Y inversé pour le nord

                detectionMessage = String.format("Target detected by the %d drone", bird.getIndex()+1);
                detectioncoor = String.format("Coordinates: X=%.1fm; Y=%.1fm",gpsX, gpsY);
            }


            gui.getDrawPanel().markVisited(bird.getX(), bird.getY());
        }
        repaint();
    }

    // ===================== RESET =====================
    public void reset() {
        birds.clear();

        DrawPanel dp = gui.getDrawPanel();
        int cx = dp.getWidth() / 2;
        int cy = dp.getHeight() / 2;
        int inner = dp.getInnerSquareSize();

        dp.resetGrid(); // Nettoie le carré blanc

        double bottomY = cy + inner / 2.0;
        currentDroneCount = DronePhysics.droneCount();

        for (int i = 0; i < currentDroneCount; i++) {
            birds.add(new ScriptedBirdie(cx, bottomY, this, i));
        }

        detectionMessage = "Target : searching...";
        detectioncoor = " X = 0; Y = 0";
        dp.resetGrid(); // Cela va aussi replacer la cible

        repaint();
    }

    public boolean allDronesReachedStep(int targetStep) {
        for (ScriptedBirdie bird : birds) {
            if (bird.getStep() < targetStep) {
                return false; // Au moins un drone n'est pas encore arrivé
            }
        }
        return true; // Tout le monde est en position
    }
}
