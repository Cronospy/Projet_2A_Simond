package com.GUI;

import com.Birds.DronePhysics;

import javax.swing.*;
import java.awt.*;

public class GUI extends JFrame {

    private DrawPanel drawPanel;
    private Sky sky;
    private Timer timer;

    private JPanel tools = new JPanel();
    private JButton step = new JButton("Start / Stop");
    private JButton reset = new JButton("Reset");
    private JSlider altitudeSlider = new JSlider(JSlider.HORIZONTAL, 2, 10,5);
    private JLabel altitudeLabel = new JLabel("Height : 5 m");

    // Chronomètre
    private JLabel timeLabel = new JLabel("Time: 0.0 s");
    private long startTime = 0;
    private boolean running = false;

    public GUI() {
        super("A flock of birdies");
        drawGUI();
        setVisible(true);
    }

    private void drawGUI() {

        setSize(1500, 900);
        setLocationRelativeTo(null);
        setDefaultCloseOperation(EXIT_ON_CLOSE);
        setLayout(new BorderLayout());

        // Couches
        JLayeredPane layeredPane = new JLayeredPane();
        add(layeredPane, BorderLayout.CENTER);

        // Fond
        drawPanel = new DrawPanel();
        drawPanel.setBounds(0, 0, 1500, 850);
        layeredPane.add(drawPanel, Integer.valueOf(0));

        // Oiseaux
        sky = new Sky(this); // 32 oiseaux par défaut
        sky.setBounds(0, 0, 1500, 850);
        layeredPane.add(sky, Integer.valueOf(1));

        // Synchronisation des tailles
        layeredPane.addComponentListener(new java.awt.event.ComponentAdapter() {
            public void componentResized(java.awt.event.ComponentEvent e) {
                Dimension d = layeredPane.getSize();
                drawPanel.setBounds(0, 0, d.width, d.height);
                sky.setBounds(0, 0, d.width, d.height);
            }
        });

        //  Timer (mise à jour des oiseaux + chronomètre)
        timer = new Timer(20, e -> {
            update();
            updateTime();
        });

        altitudeSlider.setMajorTickSpacing(1);
        altitudeSlider.setPaintTicks(true);
        altitudeSlider.setPaintLabels(true);

        altitudeSlider.addChangeListener(e -> {
            int altitude = altitudeSlider.getValue();
            DronePhysics.setAltitude(altitude); // Met à jour L et L95
            altitudeLabel.setText("Height : " + altitude + " m");

            sky.reset(); // Recrée les drones et appelle drawPanel.resetGrid()
        });

        // Outils
        tools.add(step);
        tools.add(reset);
        tools.add(altitudeLabel);
        tools.add(altitudeSlider);
        tools.add(timeLabel); // affichage du chronomètre
        add(tools, BorderLayout.SOUTH);

        step.addActionListener(e -> startStop());

        // Reset fixe
        reset.addActionListener(e -> {
            sky.reset();
            startTime = System.currentTimeMillis();
            running = false;
            timeLabel.setText("Time: 0.0 s");
        });
    }

    private void update() {
        sky.update();
        sky.repaint();
    }

    //  Mise à jour du chronomètre
    private void updateTime() {
        if (running) {
            long now = System.currentTimeMillis();
            double elapsedSeconds = (now - startTime) / 1000.0;
            timeLabel.setText(String.format("Time: %.1f s", elapsedSeconds));
        }
    }

    //  Start / Stop
    private void startStop() {
        if (timer.isRunning()) {
            timer.stop();
            running = false;
        } else {
            startTime = System.currentTimeMillis();
            running = true;
            timer.start();
        }
    }


    // Getter pour DrawPanel
    public DrawPanel getDrawPanel() {
        return drawPanel;
    }

    // NOUVEAUX GETTERS pour Sky
    public boolean isRunning() {
        return running;
    }

    public long getStartTime() {
        return startTime;
    }


}