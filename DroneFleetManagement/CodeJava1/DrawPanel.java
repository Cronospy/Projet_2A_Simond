package com.GUI;

import com.Birds.DronePhysics;

import javax.swing.*;
import java.awt.*;
import java.awt.image.BufferedImage;

public class DrawPanel extends JPanel {
    private BufferedImage canvas;
    private Graphics2D gCanvas;
    private int innerSquareSize = 800;

    // Ajout des coordonnées de la cible (en pixels, relatives au canvas)
    private int targetX, targetY;
    private boolean targetFound = false;

    public DrawPanel() {
        setBackground(Color.DARK_GRAY);
        resetGrid(); // Initialise le rectangle blanc
    }

    public void resetGrid() {
        // On crée une image de la taille du carré
        canvas = new BufferedImage(innerSquareSize, innerSquareSize, BufferedImage.TYPE_INT_ARGB);
        gCanvas = canvas.createGraphics();

        // On peint le fond en blanc (zone à explorer)
        gCanvas.setColor(Color.WHITE);
        gCanvas.fillRect(0, 0, innerSquareSize, innerSquareSize);

        //NOUVEAU : Placer la cible aléatoirement
        java.util.Random rand = new java.util.Random();
        // On garde une marge de 50px des bords pour qu'elle soit visible
        targetX = 50 + rand.nextInt(innerSquareSize - 100);
        targetY = 50 + rand.nextInt(innerSquareSize - 100);
        targetFound = false; // Reset de l'état de détection

        repaint();
    }



    @Override
    protected void paintComponent(Graphics g) {
        super.paintComponent(g);
        Graphics2D g2 = (Graphics2D) g.create();

        int cx = getWidth() / 2;
        int cy = getHeight() / 2;
        int inX = cx - innerSquareSize / 2;
        int inY = cy - innerSquareSize / 2;

        // Dessiner l'image (le carré blanc avec les traces vertes)
        if (canvas != null) {
            g.drawImage(canvas, inX, inY, null);
        }

        // NOUVEAU : Dessiner la cible (Point Rouge)
        g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);

        // Si trouvée, elle devient bleu, sinon rouge
        g2.setColor(targetFound ? new Color(0, 0, 150) : Color.RED);

        // Dessiner un cercle de 15px centré sur targetX, targetY
        g2.fillOval(inX + targetX - 7, inY + targetY - 7, 2, 2);

        // Petit contour pour la visibilité
        g2.setColor(Color.BLACK);
        g2.drawOval(inX + targetX - 7, inY + targetY - 7, 2, 2);

        // Contour noir
        g.setColor(Color.BLACK);
        g.drawRect(inX, inY, innerSquareSize, innerSquareSize);

        drawOrigin(g2, inX, inY);


        // ÉCHELLE : 1 km au-dessus du carré
        g2.setFont(new Font("Arial", Font.BOLD, 14));
        String scaleText = "1 km";
        FontMetrics fm = g2.getFontMetrics();
        int textX = cx - fm.stringWidth(scaleText) / 2;
        int textY = inY - 10;
        g2.setColor(Color.WHITE);
        g2.drawString(scaleText, textX, textY);

        // ÉCHELLE : 1 km sur le côté droit
        int textXRight = inX + innerSquareSize + 5; // juste à droite du carré
        int textYRight = inY + innerSquareSize / 2 + fm.getAscent() / 2; // milieu vertical
        g2.drawString("1 km", textXRight, textYRight);

        // Barre d’échelle 250 m
        int scaleBarWidth = innerSquareSize / 4; // 250 m
        int scaleBarX = inX + 20;
        int scaleBarY = inY + innerSquareSize - 20;
        g2.setColor(Color.BLACK);
        g2.drawLine(scaleBarX, scaleBarY, scaleBarX + scaleBarWidth, scaleBarY);
        g2.drawLine(scaleBarX, scaleBarY - 5, scaleBarX, scaleBarY + 5);
        g2.drawLine(scaleBarX + scaleBarWidth, scaleBarY - 5,
                scaleBarX + scaleBarWidth, scaleBarY + 5);
        g2.drawString("250 m", scaleBarX, scaleBarY - 8);



        // contour
        g2.setColor(Color.BLACK);
        g2.drawRect(inX, inY, innerSquareSize, innerSquareSize);

        g2.dispose();
    }

    private void drawOrigin(Graphics2D g2, int inX, int inY) {
        // Position de l'origine (bas gauche du carré blanc)
        int oX = inX;
        int oY = inY + innerSquareSize;
        int size = 40; // Longueur des flèches

        g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);

        // --- AXE X (Rouge ou Noir) ---
        g2.setColor(Color.BLACK);
        g2.setStroke(new BasicStroke(2f));
        // Ligne vers la droite
        g2.drawLine(oX, oY, oX + size, oY);
        // Petite flèche X
        g2.drawLine(oX + size, oY, oX + size - 5, oY - 3);
        g2.drawLine(oX + size, oY, oX + size - 5, oY + 3);

        // --- AXE Y (Noir) ---
        // Ligne vers le haut
        g2.drawLine(oX, oY, oX, oY - size);
        // Petite flèche Y
        g2.drawLine(oX, oY - size, oX - 3, oY - size + 5);
        g2.drawLine(oX, oY - size, oX + 3, oY - size + 5);

        // --- TEXTES (0, x, y) ---
        g2.setColor(Color.WHITE);
        g2.setFont(new Font("Arial", Font.BOLD, 12));
        g2.drawString("0", oX - 12, oY + 12); // Point Origine
        g2.drawString("x", oX + size + 5, oY + 15);
        g2.drawString("y", oX - 15, oY - size - 5);
    }

    // ==================== MARQUAGE ====================
    public void markVisited(double x, double y) {
        int cx = getWidth() / 2;
        int cy = getHeight() / 2;
        int inX = cx - innerSquareSize / 2;
        int inY = cy - innerSquareSize / 2;

        // Calcul de la largeur réelle L en pixels
        double L = DronePhysics.lenghtOnGround();
        int widthPx = (int)(((L / 1000.0) * innerSquareSize)+1);

        // On dessine un petit rectangle vert sur l'image à la position du drone
        gCanvas.setColor(new Color(170, 230, 170));

        // Centrer le tracé sur le drone
        int drawX = (int)(x - inX - widthPx / 2.0);
        int drawY = (int)(y - inY - widthPx / 2.0);

        gCanvas.fillRect(drawX, drawY, widthPx, widthPx);
    }

    // ==================== GETTERS ====================
    public int getInnerSquareSize() {
        return innerSquareSize;
    }

    public double getKmPerPixel() {
        return 1.0 / innerSquareSize;
    }


    public int getTargetX() { return targetX; }
    public int getTargetY() { return targetY; }
    public void setTargetFound(boolean found) { this.targetFound = found; repaint(); }
    public boolean isTargetFound() {
        return targetFound;
    }

    /**
     * Retourne les coordonnées réelles de la cible en mètres (0 à 1000)
     * pour l'affichage comparatif.
     */
    public double getRealTargetX_Meters() {
        return (targetX / (double) innerSquareSize) * 1000.0;
    }

    public double getRealTargetY_Meters() {
        return (1.0 - (targetY / (double) innerSquareSize)) * 1000.0;
    }

}