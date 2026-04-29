/**/

//fait le tour des obstacle
import java.awt.BasicStroke;
import java.awt.BorderLayout;
import java.awt.Color;
import java.awt.Font;
import java.awt.Graphics;
import java.awt.Graphics2D;
import java.awt.Point;
import java.awt.RenderingHints;
import java.awt.geom.AffineTransform;
import java.awt.geom.Path2D;
import java.awt.geom.Point2D;
import java.awt.image.BufferedImage;
import java.util.ArrayList;
import java.util.List;

import javax.swing.ButtonGroup;
import javax.swing.JButton;
import javax.swing.JFrame;
import javax.swing.JLabel;
import javax.swing.JPanel;
import javax.swing.JRadioButton;
import javax.swing.JTextField;
import javax.swing.Timer;

public class Simulation2D extends JPanel {
    private List<DroneState> flotte;
    private List<Obstacle> obstacles;
    private List<List<Point>> tousLesChemins;
    private List<Integer> indexCible;
    private BufferedImage canvas;
    private Graphics2D canvasGraphics;

    private boolean missionActive = false;
    private int nbDronesActuels = 10;
    private final double VITESSE_BASE = 3.5;
    private final int FREQUENCE_CALCUL = 60; // 60 frames = 1 seconde
    private final Point BASE_CENTRE = new Point(400, 550);

    private JTextField inputNbDrones;
    private JRadioButton rbRect, rbCircle;

    public Simulation2D() {
        setLayout(new BorderLayout());
        setupUI();
        initialiserFlotte(10);
        new Timer(16, e -> { if (missionActive) updateLogic(); repaint(); }).start();
    }

    private void setupUI() {
        JPanel p = new JPanel();
        p.setBackground(new Color(40, 45, 50));

        // 1. Nombre de drones
        p.add(new JLabel("<html><font color='white'>Drones :</font></html>"));
        inputNbDrones = new JTextField("10", 3);
        p.add(inputNbDrones);

        JButton btnUpdate = new JButton("Update");
        btnUpdate.addActionListener(e -> {
            missionActive = false;
            try { initialiserFlotte(Integer.parseInt(inputNbDrones.getText())); }
            catch (Exception ex) { initialiserFlotte(10); }
        });
        p.add(btnUpdate);

        // 2. Types de Trajectoire
        rbRect = new JRadioButton("Grid", true);
        rbCircle = new JRadioButton("Circles");
        rbRect.setForeground(Color.WHITE); rbCircle.setForeground(Color.WHITE);
        rbRect.setOpaque(false); rbCircle.setOpaque(false);
        ButtonGroup g = new ButtonGroup(); g.add(rbRect); g.add(rbCircle);
        p.add(rbRect); p.add(rbCircle);

        // 3. Boutons Actions
        JButton btnObs = new JButton("New Obstacles");
        btnObs.addActionListener(e -> { obstacles = new ArrayList<>(); for(int i=0; i<5; i++) obstacles.add(new Obstacle()); });
        p.add(btnObs);

        JButton btnLaunch = new JButton("Takeoff");
        btnLaunch.setBackground(new Color(80, 180, 80));
        btnLaunch.addActionListener(e -> { preparerMission(); missionActive = true; });
        p.add(btnLaunch);


        // 🔴 BOUTON STOP
        JButton btnStop = new JButton("STOP");
        btnStop.setBackground(new Color(220, 50, 50));
        btnStop.setForeground(Color.WHITE);
        btnStop.addActionListener(e -> missionActive = false);
        p.add(btnStop);

        add(p, BorderLayout.NORTH);

    }

    private void initialiserFlotte(int n) {
        nbDronesActuels = Math.min(Math.max(1, n), 100);
        flotte = FleetManager.createMatrixFleet(nbDronesActuels, BASE_CENTRE.x, BASE_CENTRE.y);
        indexCible = new ArrayList<>();
        for(int i=0; i<nbDronesActuels; i++) indexCible.add(0);
        obstacles = new ArrayList<>();
        for(int i=0; i<5; i++) obstacles.add(new Obstacle());
        canvas = new BufferedImage(800, 600, BufferedImage.TYPE_INT_ARGB);
        canvasGraphics = canvas.createGraphics();
        canvasGraphics.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
    }

    private void preparerMission() {
        tousLesChemins = new ArrayList<>();
        for (int i = 1; i <= nbDronesActuels; i++) {
            if (rbRect.isSelected()) tousLesChemins.add(PathGenerator.generateFullRectPath(i, nbDronesActuels));
            else tousLesChemins.add(PathGenerator.generateCircularPath(i, nbDronesActuels));
        }
    }

    private void updateLogic() {
        boolean missionFinie = true;

        for (int i = 0; i < flotte.size(); i++) {
            DroneState d = flotte.get(i);
            List<Point> path = tousLesChemins.get(i);
            int idx = indexCible.get(i);
            boolean enMission = idx < path.size();
            Point target = enMission ? path.get(idx) : new Point((int)d.x_initiale, (int)d.y_initiale);

            // --- DÉTECTION CAS CRITIQUE ---
            d.historiquePositions.add(new Point2D.Double(d.x, d.y));
            if (d.historiquePositions.size() > FREQUENCE_CALCUL) {
                d.historiquePositions.removeFirst();
                Point2D.Double posAncienne = d.historiquePositions.getFirst();
                double deplacement = posAncienne.distance(d.x, d.y);

                if (enMission && deplacement < 5.0 && d.z < 90) {
                    d.enUrgenceAltitude = true;
                }
            }

            // --- NAVIGATION ---
            double dx = target.x - d.x;
            double dy = target.y - d.y;
            double distCible = Math.sqrt(dx * dx + dy * dy);

            double vx = (distCible > 0.1) ? (dx / distCible) * VITESSE_BASE : 0;
            double vy = (distCible > 0.1) ? (dy / distCible) * VITESSE_BASE : 0;

            boolean auDessusRectangle = false;

            // --- GESTION OBSTACLES ---
            if (!d.enUrgenceAltitude) {
                for (Obstacle obs : obstacles) {

                    if (obs.isCircle) {
                        // 🔵 CONTOURNEMENT
                        double cx = obs.x;
                        double cy = obs.y;

                        double distO = Math.sqrt(Math.pow(d.x - cx, 2) + Math.pow(d.y - cy, 2));
                        double limite = obs.radius + 15;

                        if (distO < limite) {
                            vx += ((d.x - cx) / distO) * 4.0;
                            vy += ((d.y - cy) / distO) * 4.0;

                            if (!obs.scanComplet && distO < limite + 5) {
                                obs.scanComplet = true;
                            }
                        }

                    } else {
                        // 🟥 SURVOL RECTANGLE
                        boolean proche =
                                d.x > obs.x - 10 && d.x < obs.x + obs.width + 10 &&
                                        d.y > obs.y - 10 && d.y < obs.y + obs.height + 10;

                        if (proche) {
                            d.enUrgenceAltitude = true;
                            auDessusRectangle = true;
                        }
                    }
                }
            }

            // --- ALTITUDE ---
            double targetZ;

            if (d.enUrgenceAltitude) {
                targetZ = 100.0;
            } else {
                targetZ = enMission ? 25.0 : 0.0;
            }

            // Fin du mode urgence seulement après montée + sortie
            if (d.enUrgenceAltitude && d.z > 95 && !auDessusRectangle) {
                d.enUrgenceAltitude = false;
            }

            // --- PHYSIQUE ---
            int ox = (int)d.x, oy = (int)d.y;

            d.x += vx;
            d.y += vy;
            d.vx = vx;
            d.vy = vy;

            if (d.z < targetZ) d.z += 1.5;
            else if (d.z > targetZ) d.z -= 0.6;

            // --- PEINTURE ---
            if (enMission) {
                canvasGraphics.setStroke(new BasicStroke((float)(12 + d.z / 10.0),
                        BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
                canvasGraphics.setColor(new Color(0, 150, 255, 60));
                canvasGraphics.drawLine(ox, oy, (int)d.x, (int)d.y);
                missionFinie = false;
            } else if (distCible > 5 || d.z > 1) {
                missionFinie = false;
            }

            if (distCible < 6 && enMission) {
                indexCible.set(i, idx + 1);
            }
        }

        if (missionFinie) missionActive = false;
    }


    @Override
    protected void paintComponent(Graphics g) {
        super.paintComponent(g);
        Graphics2D g2 = (Graphics2D) g;
        g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g2.drawImage(canvas, 0, 0, null);
        for (Obstacle obs : obstacles) obs.draw(g2);

        // Zone rouge de mission
        g2.setColor(new Color(255, 0, 0, 100));
        g2.setStroke(new BasicStroke(2));
        g2.drawRect(FleetManager.RECT_X, FleetManager.RECT_Y, FleetManager.RECT_W, FleetManager.RECT_H);

        for (DroneState d : flotte) {
            double angle = Math.atan2(d.vy, d.vx);
            AffineTransform tx = new AffineTransform();
            tx.translate(d.x, d.y); tx.rotate(angle);

            Path2D.Double arrow = new Path2D.Double();
            arrow.moveTo(10, 0); arrow.lineTo(-7, -6); arrow.lineTo(-3, 0); arrow.lineTo(-7, 6); arrow.closePath();

            if (d.enUrgenceAltitude) g2.setColor(Color.RED);
            else {
                float r = (float) Math.min(1.0, d.z / 100.0);
                g2.setColor(new Color((int)(r*160), (int)(100+r*155), (int)(150+r*105)));
            }
            g2.fill(tx.createTransformedShape(arrow));
            g2.setColor(Color.BLACK); g2.draw(tx.createTransformedShape(arrow));

            g2.setFont(new Font("Monospaced", Font.BOLD, 10));
            g2.drawString(String.format("Z:%.0fm", d.z), (int)d.x+12, (int)d.y-5);
            g2.drawString(String.format("v:%.1f", Math.sqrt(d.vx*d.vx+d.vy*d.vy)), (int)d.x+12, (int)d.y+7);
        }
    }

    public static void main(String[] args) {
        JFrame f = new JFrame("Drone Simulation Pro - Cas Critique");
        f.add(new Simulation2D()); f.setSize(800, 650);
        f.setDefaultCloseOperation(JFrame.EXIT_ON_CLOSE);
        f.setLocationRelativeTo(null); f.setVisible(true);
    }
}
