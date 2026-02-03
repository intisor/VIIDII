// chartInterop.js - Chart.js interop for Session Recap

window.chartInterop = (function () {
    let chartInstance = null;

    function createScoreChart(canvasId, participantNames, scores) {
        // Clean up existing chart
        if (chartInstance) {
            chartInstance.destroy();
            chartInstance = null;
        }

        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            console.error(`Canvas element '${canvasId}' not found`);
            return;
        }

        const ctx = canvas.getContext('2d');
        
        chartInstance = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: participantNames,
                datasets: [{
                    label: 'Attendance Score (%)',
                    data: scores,
                    backgroundColor: scores.map(score => {
                        if (score >= 80) return 'rgba(34, 197, 94, 0.7)'; // green
                        if (score >= 70) return 'rgba(234, 179, 8, 0.7)'; // yellow
                        return 'rgba(239, 68, 68, 0.7)'; // red
                    }),
                    borderColor: scores.map(score => {
                        if (score >= 80) return 'rgb(34, 197, 94)'; // green
                        if (score >= 70) return 'rgb(234, 179, 8)'; // yellow
                        return 'rgb(239, 68, 68)'; // red
                    }),
                    borderWidth: 2,
                    borderRadius: 8,
                    borderSkipped: false,
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: false
                    },
                    tooltip: {
                        backgroundColor: 'rgba(0, 0, 0, 0.8)',
                        titleColor: '#fff',
                        bodyColor: '#fff',
                        borderColor: 'rgba(255, 255, 255, 0.2)',
                        borderWidth: 1,
                        padding: 12,
                        displayColors: false,
                        callbacks: {
                            label: function(context) {
                                return `Score: ${context.parsed.y.toFixed(1)}%`;
                            }
                        }
                    }
                },
                scales: {
                    y: {
                        beginAtZero: true,
                        max: 100,
                        ticks: {
                            callback: function(value) {
                                return value + '%';
                            },
                            color: '#6b7280'
                        },
                        grid: {
                            color: 'rgba(0, 0, 0, 0.05)',
                            drawBorder: false
                        }
                    },
                    x: {
                        ticks: {
                            color: '#6b7280',
                            maxRotation: 45,
                            minRotation: 45
                        },
                        grid: {
                            display: false,
                            drawBorder: false
                        }
                    }
                }
            }
        });

        console.log('Score chart created successfully');
    }

    function destroyChart() {
        if (chartInstance) {
            chartInstance.destroy();
            chartInstance = null;
            console.log('Chart destroyed');
        }
    }

    return {
        createScoreChart: createScoreChart,
        destroyChart: destroyChart
    };
})();
